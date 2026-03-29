using Maple2.Database.Extensions;
using Maple2.Database.Model;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public TimeCardEntry? GetTimeCard(string cardCode) {
            return Context.TimeCard
                .Where(card => card.CardCode == cardCode)
                .Select(Project())
                .FirstOrDefault();
        }

        public IList<TimeCardEntry> ListTimeCards(int limit = 100) {
            return Context.TimeCard
                .OrderByDescending(card => card.Id)
                .Take(limit)
                .Select(Project())
                .ToList();
        }

        public bool CreateTimeCards(IEnumerable<string> cardCodes) {
            List<TimeCard> cards = cardCodes
                .Distinct(StringComparer.Ordinal)
                .Select(code => new TimeCard {
                    CardCode = code,
                })
                .ToList();

            Context.TimeCard.AddRange(cards);
            return Context.TrySaveChanges();
        }

        public bool MarkTimeCardUsed(long cardId, long accountId, string username, DateTime usedAtUtc) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            TimeCard? card = Context.TimeCard.FirstOrDefault(entry => entry.Id == cardId);
            if (card == null || card.IsUsed) {
                return false;
            }

            card.IsUsed = true;
            card.UsedAt = usedAtUtc;
            card.UsedByAccountId = accountId;
            card.UsedByUsername = username;
            Context.TimeCard.Update(card);
            return Context.TrySaveChanges();
        }

        private static Expression<Func<TimeCard, TimeCardEntry>> Project() {
            return card => new TimeCardEntry(
                card.Id,
                card.CardCode,
                card.IsUsed,
                card.UsedAt,
                card.UsedByAccountId,
                card.UsedByUsername
            );
        }
    }

    public sealed record TimeCardEntry(long Id, string CardCode, bool IsUsed, DateTime? UsedAt, long? UsedByAccountId, string? UsedByUsername);
}
