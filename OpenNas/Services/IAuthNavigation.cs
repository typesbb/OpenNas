namespace OpenNas.Services;

public interface IAuthNavigation
{
    Task GoToLoginAsync();

    Task GoToMainShellAsync();
}
