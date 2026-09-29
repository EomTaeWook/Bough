using System;
using System.Threading.Tasks;

namespace Bough.App.Presenters
{
    public class TagCreationPresenter
    {
        private readonly Func<string, Task<string>> _submit;

        public TagCreationPresenter(Func<string, Task<string>> submit)
        {
            _submit = submit ?? throw new ArgumentNullException(nameof(submit));
        }

        public bool IsRunning { get; private set; }

        public async Task<TagCreationResult> SubmitAsync(string name)
        {
            if (IsRunning)
            {
                return new TagCreationResult(false, null, null);
            }
            IsRunning = true;
            try
            {
                string failure = await _submit(name);
                return new TagCreationResult(failure == null, failure, null);
            }
            catch (Exception exception)
            {
                return new TagCreationResult(false, null, exception);
            }
            finally
            {
                IsRunning = false;
            }
        }
    }

    public class TagCreationResult
    {
        public TagCreationResult(bool succeeded, string failure, Exception error)
        {
            Succeeded = succeeded;
            Failure = failure;
            Error = error;
        }

        public bool Succeeded { get; }
        public string Failure { get; }
        public Exception Error { get; }
    }
}
