using System;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Web.Services;

public class AdminSessionService(IMemoryCache cache) {
    public const string CookieName = "ms2_admin_session";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public bool ValidatePassword(string password, out string errorMessage) {
        string? expectedPassword = Environment.GetEnvironmentVariable("WEB_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(expectedPassword)) {
            errorMessage = "后台管理功能未启用，请先设置 WEB_ADMIN_PASSWORD 环境变量。";
            return false;
        }

        if (!string.Equals(password, expectedPassword, StringComparison.Ordinal)) {
            errorMessage = "管理密码错误。";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public string CreateSession() {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set(GetCacheKey(token), true, SessionLifetime);
        return token;
    }

    public bool ValidateSession(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return false;
        }

        if (!cache.TryGetValue(GetCacheKey(token), out _)) {
            return false;
        }

        cache.Set(GetCacheKey(token), true, SessionLifetime);
        return true;
    }

    public void RemoveSession(string? token) {
        if (!string.IsNullOrWhiteSpace(token)) {
            cache.Remove(GetCacheKey(token));
        }
    }

    private static string GetCacheKey(string token) => $"admin-session:{token}";
}
