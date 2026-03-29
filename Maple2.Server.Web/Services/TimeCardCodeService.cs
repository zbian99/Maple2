using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Maple2.Server.Web.Services;

public class TimeCardCodeService {
    private static readonly IReadOnlyDictionary<int, string> DurationPrefixes = new Dictionary<int, string> {
        [1] = "MS2-01",
        [7] = "MS2-07",
        [30] = "MS2-30",
    };

    public bool IsSupportedDuration(int durationDays) => DurationPrefixes.ContainsKey(durationDays);

    public string Generate(int durationDays) {
        if (!DurationPrefixes.TryGetValue(durationDays, out string? prefix)) {
            throw new ArgumentOutOfRangeException(nameof(durationDays), "Unsupported time-card duration.");
        }

        string payload = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        return $"{prefix}-{payload[..4]}-{payload[4..8]}-{payload[8..12]}-{payload[12..16]}";
    }

    public bool TryParseDurationDays(string? cardCode, out int durationDays) {
        string normalized = (cardCode ?? string.Empty).Trim().ToUpperInvariant();
        foreach ((int days, string prefix) in DurationPrefixes) {
            if (normalized.StartsWith(prefix + "-", StringComparison.Ordinal)) {
                durationDays = days;
                return true;
            }
        }

        durationDays = 0;
        return false;
    }
}
