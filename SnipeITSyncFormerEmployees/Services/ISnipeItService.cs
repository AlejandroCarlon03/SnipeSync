using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

public interface ISnipeItService
{
    Task<SnipeItUser?> FindSnipeItUser(string fullName, string email);
    Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle);
}