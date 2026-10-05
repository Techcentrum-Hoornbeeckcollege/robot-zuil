using Zuil.Core.Abstractions;
using Zuil.Core.Models;
using Zuil.Core.Text;

namespace Zuil.Graph;

/// <summary>
/// Generates plausible meetings so the whole kiosk can be developed on a laptop
/// with no Entra tenant. Selected with --fake-calendar.
/// </summary>
public sealed class FakeCalendarSource : ICalendarSource
{
    private static readonly string[] Names =
    [
        "Jonathan Nap",
        "Sanne de Vries",
        "Ahmed El Amrani",
        "Émile Dubois",
        "Wei Zhang",
    ];

    private static readonly string[] Subjects =
    [
        "Sprint review",
        "Kennismaking",
        "Projectoverleg",
        "1:1",
    ];

    private readonly IClock _clock;

    public FakeCalendarSource(IClock clock) => _clock = clock;

    public Task<IReadOnlyList<Meeting>> GetOccurrencesAsync(
        Room room,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        // Deterministic per room, so restarts don't reshuffle the test data.
        var rng = new Random(room.Id.GetHashCode(StringComparison.Ordinal));
        var meetings = new List<Meeting>();
        var slot = _clock.Now.AddMinutes(-30);

        for (var i = 0; i < 4 && slot < to; i++)
        {
            var name = Names[rng.Next(Names.Length)];
            meetings.Add(new Meeting
            {
                Id = $"fake-{room.Id}-{i}",
                RoomId = room.Id,
                Subject = Subjects[rng.Next(Subjects.Length)],
                Start = slot,
                End = slot.AddMinutes(45),
                IsCancelled = false,
                Attendees =
                [
                    new Attendee { Name = name, NormalizedName = NameNormalizer.Normalize(name) },
                ],
            });

            slot = slot.AddMinutes(60 + rng.Next(3) * 30);
        }

        return Task.FromResult<IReadOnlyList<Meeting>>(meetings);
    }
}
