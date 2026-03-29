using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public bool CreateAccountExtraInfo(long accountId, string qqNumber, string phoneNumber, DateTime? expireAt = null) {
            var info = new AccountExtraInfo {
                AccountId = accountId,
                QqNumber = qqNumber,
                PhoneNumber = phoneNumber,
                ExpireAt = expireAt ?? DateTime.UtcNow.Add(Constant.TrialAccountDuration),
            };

            Context.AccountExtraInfo.Add(info);
            return Context.TrySaveChanges();
        }

        public DateTime? GetAccountExpireAt(long accountId) {
            return Context.AccountExtraInfo
                .Where(info => info.AccountId == accountId)
                .Select(info => (DateTime?) info.ExpireAt)
                .FirstOrDefault();
        }

        public Dictionary<long, DateTime> GetAccountExpireAtLookup(IEnumerable<long> accountIds) {
            long[] ids = accountIds.Distinct().ToArray();
            if (ids.Length == 0) {
                return [];
            }

            return Context.AccountExtraInfo
                .Where(info => ids.Contains(info.AccountId))
                .ToDictionary(info => info.AccountId, info => info.ExpireAt);
        }

        public DateTime? ExtendAccountExpireAt(long accountId, int durationDays, DateTime nowUtc) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            AccountExtraInfo? info = Context.AccountExtraInfo.FirstOrDefault(entry => entry.AccountId == accountId);
            if (info == null) {
                return null;
            }

            DateTime baseTime = info.ExpireAt > nowUtc ? info.ExpireAt : nowUtc;
            info.ExpireAt = baseTime.AddDays(durationDays);
            Context.AccountExtraInfo.Update(info);

            return Context.TrySaveChanges() ? info.ExpireAt : null;
        }

        public bool ResetPassword(string username, string? qqNumber, string? phoneNumber, string password) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            string normalizedUsername = username.Trim().ToLowerInvariant();
            string? normalizedQq = string.IsNullOrWhiteSpace(qqNumber) ? null : qqNumber.Trim();
            string? normalizedPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();

            if (normalizedQq == null && normalizedPhone == null) {
                return false;
            }

            Account? account = Context.Account
                .Join(Context.AccountExtraInfo,
                    account => account.Id,
                    info => info.AccountId,
                    (account, info) => new { account, info })
                .Where(entry => entry.account.Username == normalizedUsername &&
                                ((normalizedQq != null && entry.info.QqNumber == normalizedQq) ||
                                 (normalizedPhone != null && entry.info.PhoneNumber == normalizedPhone)))
                .Select(entry => entry.account)
                .FirstOrDefault();

            if (account == null) {
                return false;
            }

            account.Password = BCrypt.Net.BCrypt.HashPassword(password, 13);
            Context.Account.Update(account);
            return Context.TrySaveChanges();
        }
    }
}
