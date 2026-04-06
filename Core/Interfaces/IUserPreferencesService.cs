using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IUserPreferencesService
{
    UserPreferences Load();

    void Save(UserPreferences preferences);
}