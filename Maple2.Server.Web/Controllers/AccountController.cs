using System;
using System.Text.RegularExpressions;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Web.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase {
    private static readonly Regex UsernamePattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex QqPattern = new("^[1-9][0-9]{4,15}$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new("^1[3-9][0-9]{9}$", RegexOptions.Compiled);
    private readonly GameStorage gameStorage;
    private readonly TimeCardCodeService timeCardCodeService;

    public AccountController(GameStorage gameStorage, TimeCardCodeService timeCardCodeService) {
        this.gameStorage = gameStorage;
        this.timeCardCodeService = timeCardCodeService;
    }

    [HttpPost("register")]
    public ActionResult<ApiResponse> Register([FromBody] RegisterRequest request) {
        string username = NormalizeUsername(request.Username);
        string password = NormalizePassword(request.Password);
        string confirmPassword = NormalizePassword(request.ConfirmPassword);
        string qqNumber = NormalizeDigits(request.QqNumber);
        string phoneNumber = NormalizeDigits(request.PhoneNumber);

        ApiResponse? error = ValidateRegistration(username, password, confirmPassword, qqNumber, phoneNumber);
        if (error != null) {
            return BadRequest(error);
        }

        using GameStorage.Request db = gameStorage.Context();

        if (db.GetAccount(username) != null) {
            return Conflict(new ApiResponse(false, "该账号名已被注册，请更换后重试。"));
        }

        try {
            db.BeginTransaction();

            var account = new Account {
                Username = username,
                MachineId = Guid.Empty,
            };

            Account createdAccount = db.CreateAccount(account, password);
            if (!db.CreateAccountExtraInfo(createdAccount.Id, qqNumber, phoneNumber, DateTime.UtcNow.Add(Constant.TrialAccountDuration))) {
                db.Rollback();
                Log.Error("Failed to create account extra info for {Username}", username);
                return StatusCode(500, new ApiResponse(false, "注册失败，请稍后再试。"));
            }

            if (!db.Commit()) {
                Log.Error("Failed to commit account registration for {Username}", username);
                return StatusCode(500, new ApiResponse(false, "注册失败，请稍后再试。"));
            }

            return Ok(new ApiResponse(true, $"注册成功，已赠送 {Constant.TrialAccountDuration.TotalDays:0} 天游戏时间，请返回游戏客户端登录。"));
        } catch (DbUpdateException ex) {
            db.Rollback();
            Log.Warning(ex, "Registration conflict for {Username}", username);
            return Conflict(new ApiResponse(false, "该账号名已被注册，请更换后重试。"));
        } catch (Exception ex) {
            db.Rollback();
            Log.Error(ex, "Unexpected registration failure for {Username}", username);
            return StatusCode(500, new ApiResponse(false, "注册失败，请稍后再试。"));
        }
    }

    [HttpPost("reset-password")]
    public ActionResult<ApiResponse> ResetPassword([FromBody] ResetPasswordRequest request) {
        string username = NormalizeUsername(request.Username);
        string? qqNumber = NormalizeOptionalDigits(request.QqNumber);
        string? phoneNumber = NormalizeOptionalDigits(request.PhoneNumber);
        string newPassword = NormalizePassword(request.NewPassword);
        string confirmPassword = NormalizePassword(request.ConfirmPassword);

        ApiResponse? error = ValidatePasswordReset(username, qqNumber, phoneNumber, newPassword, confirmPassword);
        if (error != null) {
            return BadRequest(error);
        }

        using GameStorage.Request db = gameStorage.Context();

        try {
            bool updated = db.ResetPassword(username, qqNumber, phoneNumber, newPassword);
            if (!updated) {
                return BadRequest(new ApiResponse(false, "账号或验证信息不正确，无法重置密码。"));
            }

            return Ok(new ApiResponse(true, "密码已重置成功，请返回游戏客户端使用新密码登录。"));
        } catch (Exception ex) {
            Log.Error(ex, "Unexpected password reset failure for {Username}", username);
            return StatusCode(500, new ApiResponse(false, "密码重置失败，请稍后再试。"));
        }
    }

    [HttpPost("redeem-time-card")]
    public ActionResult<RedeemTimeCardResponse> RedeemTimeCard([FromBody] RedeemTimeCardRequest request) {
        string username = NormalizeUsername(request.Username);
        string password = NormalizePassword(request.Password);
        string cardCode = NormalizeCardCode(request.CardCode);

        ApiResponse? error = ValidateRedeemRequest(username, password, cardCode);
        if (error != null) {
            return BadRequest(RedeemTimeCardResponse.FromError(error.Message));
        }

        if (!timeCardCodeService.TryParseDurationDays(cardCode, out int durationDays)) {
            return BadRequest(RedeemTimeCardResponse.FromError("卡密不存在或已使用，请检查后重试。"));
        }

        using GameStorage.Request db = gameStorage.Context();

        try {
            Account? account = db.GetAccount(username);
            if (account == null || !db.VerifyPassword(account.Id, password)) {
                return BadRequest(RedeemTimeCardResponse.FromError("账号、密码或卡密不正确，请检查后重试。"));
            }

            GameStorage.TimeCardEntry? card = db.GetTimeCard(cardCode);
            if (card == null || card.IsUsed) {
                return BadRequest(RedeemTimeCardResponse.FromError("账号、密码或卡密不正确，请检查后重试。"));
            }

            db.BeginTransaction();

            DateTime nowUtc = DateTime.UtcNow;
            DateTime? expireAt = db.ExtendAccountExpireAt(account.Id, durationDays, nowUtc);
            if (expireAt == null) {
                db.Rollback();
                return BadRequest(RedeemTimeCardResponse.FromError("当前账号缺少点卡信息，请联系管理员处理。"));
            }

            if (!db.MarkTimeCardUsed(card.Id, account.Id, account.Username, nowUtc)) {
                db.Rollback();
                return BadRequest(RedeemTimeCardResponse.FromError("卡密不存在或已使用，请检查后重试。"));
            }

            if (!db.Commit()) {
                return StatusCode(500, RedeemTimeCardResponse.FromError("充值失败，请稍后再试。"));
            }

            return Ok(new RedeemTimeCardResponse(true, "充值成功。", durationDays, expireAt.Value));
        } catch (DbUpdateException ex) {
            db.Rollback();
            Log.Warning(ex, "Time card redeem conflict for {Username}", username);
            return BadRequest(RedeemTimeCardResponse.FromError("卡密不存在或已使用，请检查后重试。"));
        } catch (Exception ex) {
            db.Rollback();
            Log.Error(ex, "Unexpected time card redeem failure for {Username}", username);
            return StatusCode(500, RedeemTimeCardResponse.FromError("充值失败，请稍后再试。"));
        }
    }

    private static ApiResponse? ValidateRegistration(string username, string password, string confirmPassword, string qqNumber, string phoneNumber) {
        if (!IsValidUsername(username)) {
            return new ApiResponse(false, "账号名需为 3-16 位，仅支持小写字母、数字和下划线。");
        }

        if (!IsValidPassword(password)) {
            return new ApiResponse(false, "密码长度需为 6-72 位。");
        }

        if (password != confirmPassword) {
            return new ApiResponse(false, "两次输入的密码不一致。");
        }

        if (!QqPattern.IsMatch(qqNumber)) {
            return new ApiResponse(false, "请输入正确的 QQ 号。");
        }

        if (!PhonePattern.IsMatch(phoneNumber)) {
            return new ApiResponse(false, "请输入正确的手机号。");
        }

        return null;
    }

    private static ApiResponse? ValidatePasswordReset(string username, string? qqNumber, string? phoneNumber, string newPassword, string confirmPassword) {
        if (!IsValidUsername(username)) {
            return new ApiResponse(false, "请输入正确的账号名。");
        }

        if (string.IsNullOrWhiteSpace(qqNumber) && string.IsNullOrWhiteSpace(phoneNumber)) {
            return new ApiResponse(false, "QQ 号和手机号至少填写一项。");
        }

        if (!string.IsNullOrWhiteSpace(qqNumber) && !QqPattern.IsMatch(qqNumber)) {
            return new ApiResponse(false, "请输入正确的 QQ 号。");
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber) && !PhonePattern.IsMatch(phoneNumber)) {
            return new ApiResponse(false, "请输入正确的手机号。");
        }

        if (!IsValidPassword(newPassword)) {
            return new ApiResponse(false, "新密码长度需为 6-72 位。");
        }

        if (newPassword != confirmPassword) {
            return new ApiResponse(false, "两次输入的新密码不一致。");
        }

        return null;
    }

    private static ApiResponse? ValidateRedeemRequest(string username, string password, string cardCode) {
        if (!IsValidUsername(username)) {
            return new ApiResponse(false, "请输入正确的账号名。");
        }

        if (!IsValidPassword(password)) {
            return new ApiResponse(false, "请输入正确的账号密码。");
        }

        if (string.IsNullOrWhiteSpace(cardCode)) {
            return new ApiResponse(false, "请输入卡密。");
        }

        return null;
    }

    private static bool IsValidUsername(string username) {
        return username.Length is >= 3 and <= 16 && UsernamePattern.IsMatch(username);
    }

    private static bool IsValidPassword(string password) {
        return password.Length is >= 6 and <= 72;
    }

    private static string NormalizeUsername(string? username) {
        return (username ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizePassword(string? password) {
        return (password ?? string.Empty).Trim();
    }

    private static string NormalizeDigits(string? value) {
        return Regex.Replace(value ?? string.Empty, "\\s+", string.Empty);
    }

    private static string? NormalizeOptionalDigits(string? value) {
        string normalized = NormalizeDigits(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeCardCode(string? cardCode) {
        return Regex.Replace(cardCode ?? string.Empty, "\\s+", string.Empty).ToUpperInvariant();
    }

    public sealed record ApiResponse(bool Success, string Message);

    public sealed record RedeemTimeCardResponse(bool Success, string Message, int DurationDays, DateTime? ExpireAt) {
        public static RedeemTimeCardResponse FromError(string message) => new(false, message, 0, null);
    }

    public sealed class RegisterRequest {
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string ConfirmPassword { get; init; } = string.Empty;
        public string QqNumber { get; init; } = string.Empty;
        public string PhoneNumber { get; init; } = string.Empty;
    }

    public sealed class ResetPasswordRequest {
        public string Username { get; init; } = string.Empty;
        public string? QqNumber { get; init; }
        public string? PhoneNumber { get; init; }
        public string NewPassword { get; init; } = string.Empty;
        public string ConfirmPassword { get; init; } = string.Empty;
    }

    public sealed class RedeemTimeCardRequest {
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string CardCode { get; init; } = string.Empty;
    }
}
