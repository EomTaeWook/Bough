using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bough.App.Internals;

namespace Bough.App.Controls
{
    public class AuthorIdenticon : Control
    {
        public static readonly StyledProperty<string> AuthorNameProperty = AvaloniaProperty.Register<AuthorIdenticon, string>(nameof(AuthorName), string.Empty);
        public static readonly StyledProperty<string> AuthorEmailProperty = AvaloniaProperty.Register<AuthorIdenticon, string>(nameof(AuthorEmail), string.Empty);
        public static readonly StyledProperty<string> CommitHashProperty = AvaloniaProperty.Register<AuthorIdenticon, string>(nameof(CommitHash), string.Empty);
        public static readonly StyledProperty<string> GitHubRemoteUrlProperty = AvaloniaProperty.Register<AuthorIdenticon, string>(nameof(GitHubRemoteUrl), string.Empty);
        public static readonly StyledProperty<bool> ExternalPhotosEnabledProperty = AvaloniaProperty.Register<AuthorIdenticon, bool>(nameof(ExternalPhotosEnabled), false);
        public static readonly StyledProperty<AuthorPhotoSource> PhotoSourceProperty = AvaloniaProperty.Register<AuthorIdenticon, AuthorPhotoSource>(nameof(PhotoSource), AuthorPhotoSource.LocalIdenticon);

        private static readonly AuthorPhotoService _photos = new();

        private static readonly string[] _colorKeys = new string[]
        {
            "BoughBrushGraphOne",
            "BoughBrushGraphTwo",
            "BoughBrushGraphThree",
            "BoughBrushGraphFour",
            "BoughBrushSuccess",
            "BoughBrushDiffRemoved"
        };

        private string _identityKey = string.Empty;
        private byte[] _digest = Array.Empty<byte>();
        private Bitmap _photo;
        private bool _isAttached;
        private int _photoRequest;

        static AuthorIdenticon()
        {
            AffectsRender<AuthorIdenticon>(AuthorNameProperty, AuthorEmailProperty);
        }

        public string AuthorName
        {
            get { return GetValue(AuthorNameProperty); }
            set { SetValue(AuthorNameProperty, value); }
        }

        public string AuthorEmail
        {
            get { return GetValue(AuthorEmailProperty); }
            set { SetValue(AuthorEmailProperty, value); }
        }

        public AuthorIdenticon()
        {
            ActualThemeVariantChanged += (sender, eventArgs) => InvalidateVisual();
        }

        public string CommitHash
        {
            get { return GetValue(CommitHashProperty); }
            set { SetValue(CommitHashProperty, value); }
        }

        public string GitHubRemoteUrl
        {
            get { return GetValue(GitHubRemoteUrlProperty); }
            set { SetValue(GitHubRemoteUrlProperty, value); }
        }

        public bool ExternalPhotosEnabled
        {
            get { return GetValue(ExternalPhotosEnabledProperty); }
            set { SetValue(ExternalPhotosEnabledProperty, value); }
        }

        public AuthorPhotoSource PhotoSource
        {
            get { return GetValue(PhotoSourceProperty); }
            private set { SetValue(PhotoSourceProperty, value); }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
        {
            base.OnAttachedToVisualTree(eventArgs);
            _isAttached = true;
            GitHubAuthorLinkSettings.Changed += OnGitHubAuthorLinkChanged;
            RefreshPhoto();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
        {
            _isAttached = false;
            GitHubAuthorLinkSettings.Changed -= OnGitHubAuthorLinkChanged;
            RefreshPhoto();
            base.OnDetachedFromVisualTree(eventArgs);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == AuthorNameProperty || change.Property == AuthorEmailProperty || change.Property == CommitHashProperty || change.Property == GitHubRemoteUrlProperty || change.Property == ExternalPhotosEnabledProperty)
            {
                RefreshPhoto();
            }
        }

        private void OnGitHubAuthorLinkChanged()
        {
            if (Dispatcher.UIThread.CheckAccess() == false)
            {
                Dispatcher.UIThread.Post(RefreshPhoto);
                return;
            }

            RefreshPhoto();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            double side = Math.Min(Bounds.Width, Bounds.Height);
            if (side <= 0)
            {
                return;
            }

            EnsureDigest();
            double left = (Bounds.Width - side) / 2;
            double top = (Bounds.Height - side) / 2;
            Point center = new(left + side / 2, top + side / 2);
            IBrush background = GetResourceBrush("BoughBrushInset");
            Pen outline = new(GetResourceBrush("BoughBrushBorderStrong"), 1);
            context.DrawEllipse(background, outline, center, side / 2 - 0.5, side / 2 - 0.5);

            if (_photo != null)
            {
                Rect destination = new(left, top, side, side);
                using (context.PushGeometryClip(new EllipseGeometry(destination)))
                {
                    context.DrawImage(_photo, destination);
                }
                return;
            }

            double cell = side * 0.138;
            double inset = (side - cell * 5) / 2;
            double gap = Math.Max(0.15, side * 0.008);
            IBrush foreground = GetResourceBrush(_colorKeys[_digest[0] % _colorKeys.Length]);
            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    int mirroredColumn = column;
                    if (column > 2)
                    {
                        mirroredColumn = 4 - column;
                    }
                    int bitIndex = row * 3 + mirroredColumn;
                    if ((_digest[1 + bitIndex / 8] & (1 << (bitIndex % 8))) == 0)
                    {
                        continue;
                    }
                    Rect tile = new(left + inset + column * cell + gap,
                        top + inset + row * cell + gap, cell - gap * 2, cell - gap * 2);
                    context.DrawRectangle(foreground, null, tile);
                }
            }
        }

        private void RefreshPhoto()
        {
            _photoRequest++;
            _photo?.Dispose();
            _photo = null;
            PhotoSource = AuthorPhotoSource.LocalIdenticon;
            InvalidateVisual();
            if (_isAttached == false)
            {
                return;
            }
            if (ExternalPhotosEnabled == false)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(AuthorEmail))
            {
                if (string.IsNullOrWhiteSpace(CommitHash))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(GitHubRemoteUrl))
                {
                    return;
                }
            }

            _ = LoadPhotoAsync(AuthorEmail, CommitHash, GitHubRemoteUrl, _photoRequest);
        }

        private IBrush GetResourceBrush(string key)
        {
            if (this.TryFindResource(key, ActualThemeVariant, out object resource) == true)
            {
                if (resource is IBrush brush)
                {
                    return brush;
                }
            }

            return Brushes.Gray;
        }

        private async System.Threading.Tasks.Task LoadPhotoAsync(string email, string commitHash, string gitHubRemoteUrl, int request)
        {
            try
            {
                AuthorPhotoResult result = await _photos.GetAuthorPhotoAsync(email, commitHash, gitHubRemoteUrl, true).ConfigureAwait(false);
                if (result.Data == null)
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() => ApplyPhoto(result, request));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return;
            }
        }

        private void ApplyPhoto(AuthorPhotoResult result, int request)
        {
            if (request != _photoRequest)
            {
                return;
            }
            if (_isAttached == false)
            {
                return;
            }
            if (ExternalPhotosEnabled == false)
            {
                return;
            }
            try
            {
                using MemoryStream stream = new(result.Data, false);
                _photo = new Bitmap(stream);
                PhotoSource = result.Source;
                InvalidateVisual();
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                // Invalid image data leaves the local identicon visible.
            }
        }

        private void EnsureDigest()
        {
            string email = AuthorEmail ?? string.Empty;
            string name = AuthorName ?? string.Empty;
            string key;
            if (string.IsNullOrWhiteSpace(email) == false)
            {
                key = $"email:{email.Trim().ToLowerInvariant()}";
            }
            else if (string.IsNullOrWhiteSpace(name) == false)
            {
                key = $"name:{name.Trim().ToLowerInvariant()}";
            }
            else
            {
                key = "unknown author";
            }

            if (key == _identityKey)
            {
                return;
            }
            _identityKey = key;
            _digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        }
    }
}
