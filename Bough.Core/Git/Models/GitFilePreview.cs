using System;
using System.Collections.Generic;

namespace Bough.Core.Git.Models
{
    public class GitFilePreview
    {
        public GitFilePreview(string text, string descriptionCode, bool isBinary, long size, params object[] descriptionArguments)
        {
            Text = text;
            DescriptionCode = descriptionCode;
            object[] arguments = Array.Empty<object>();
            if (descriptionArguments != null)
            {
                arguments = (object[])descriptionArguments.Clone();
            }
            DescriptionArguments = Array.AsReadOnly(arguments);
            IsBinary = isBinary;
            Size = size;
        }

        public string Text { get; }

        public string DescriptionCode { get; }
        public IReadOnlyList<object> DescriptionArguments { get; }

        public bool IsBinary { get; }

        public long Size { get; }
    }
}
