namespace Bough.App.ViewModels.Models
{
    public class GitRevertChoice
    {
        public GitRevertChoice(int mainlineParent)
        {
            MainlineParent = mainlineParent;
        }

        public int MainlineParent { get; }
    }
}
