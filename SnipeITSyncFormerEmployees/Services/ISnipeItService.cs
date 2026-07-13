namespace SnipeITSyncFormerEmployees;

public interface ISnipeItService
{
    Task<SnipeItUser?> FindSnipeItUser(string fullName, string email);
    Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle);
    Task<bool> CreateSnipeItUser(string firstName, string lastName, string email, string username, string jobTitle);
}