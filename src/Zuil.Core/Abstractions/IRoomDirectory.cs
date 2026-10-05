using Zuil.Core.Models;

namespace Zuil.Core.Abstractions;

/// <summary>
/// The six rooms this kiosk serves, loaded from config/rooms.json at startup.
/// </summary>
public interface IRoomDirectory
{
    IReadOnlyList<Room> All { get; }

    Room? ById(string id);

    Room? ByMailbox(string mailbox);
}
