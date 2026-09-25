using Bough.App.Internals;

namespace Bough.App.Controls
{
    public class AuthorPhotoResult
    {
        public AuthorPhotoResult(byte[] data, AuthorPhotoSource source)
        {
            Data = data;
            Source = source;
        }

        public byte[] Data { get; }

        public AuthorPhotoSource Source { get; }
    }
}
