using Zuil.Core.Models;

namespace Zuil.Core.Abstractions;

/// <summary>
/// The six rooms this kiosk serves, as last saved under Instellingen → Zalen.
/// </summary>
public interface IRoomDirectory
{
    IReadOnlyList<Room> All { get; }

    Room? ById(string id);

    Room? ByMailbox(string mailbox);
}
