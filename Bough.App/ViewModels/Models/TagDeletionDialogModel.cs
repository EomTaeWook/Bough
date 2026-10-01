using System;
using Bough.Core.Git.Models;

namespace Bough.App.ViewModels.Models
{
    public class TagDeletionDialogModel
    {
        public bool IsRemoteMode { get; internal set; }
        public string RemoteName { get; internal set; }
        public GitRemoteTagDeletionPreview RemotePreview { get; internal set; }
        public bool RemoteConfirmed { get; internal set; }
        public bool IsLookingUp { get; internal set; }
        public bool IsDeleting { get; internal set; }
        public bool IsClosed { get; internal set; }
        public Exception Error { get; internal set; }
        public string FailureMessage { get; internal set; }

        public bool CanDelete
        {
            get
            {
                if (IsClosed)
                {
                    return false;
                }
                if (IsDeleting)
                {
                    return false;
                }
                if (IsLookingUp)
                {
                    return false;
                }
                if (IsRemoteMode == false)
                {
                    return true;
                }
                if (RemotePreview == null)
                {
                    return false;
                }
                if (RemotePreview.RemoteName != RemoteName)
                {
                    return false;
                }
                return RemoteConfirmed;
            }
        }
    }
}
