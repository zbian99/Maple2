using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Server.Web.Services;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Web.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase {
    private readonly AdminSessionService adminSessionService;
    private readonly GameStorage gameStorage;
    private readonly TimeCardCodeService timeCardCodeService;

    public AdminController(AdminSessionService adminSessionService, GameStorage gameStorage, TimeCardCodeService timeCardCodeService) {
        this.adminSessionService = adminSessionService;
        this.gameStorage = gameStorage;
        this.timeCardCodeService = timeCardCodeService;
    }

    [HttpPost("login")]
    public ActionResult<ApiResponse> Login([FromBody] AdminLoginRequest request) {
        string password = (request.Password ?? string.Empty).Trim();
        if (!adminSessionService.ValidatePassword(password, out string errorMessage)) {
            return BadRequest(new ApiResponse(false, errorMessage));
        }

        string sessionToken = adminSessionService.CreateSession();
        Response.Cookies.Append(AdminSessionService.CookieName, sessionToken, BuildCookieOptions());
        return Ok(new ApiResponse(true, "后台登录成功。"));
    }

    [HttpPost("logout")]
    public ActionResult<ApiResponse> Logout() {
        string? token = Request.Cookies[AdminSessionService.CookieName];
        adminSessionService.RemoveSession(token);
        Response.Cookies.Delete(AdminSessionService.CookieName);
        return Ok(new ApiResponse(true, "已退出后台登录。"));
    }

    [HttpGet("time-cards")]
    public ActionResult<TimeCardListResponse> ListTimeCards() {
        if (!IsAuthorized()) {
            return Unauthorized(TimeCardListResponse.FromError("请先登录后台。"));
        }

        using GameStorage.Request db = gameStorage.Context();
        IList<GameStorage.TimeCardEntry> cards = db.ListTimeCards();
        List<TimeCardItem> items = cards.Select(card => {
            timeCardCodeService.TryParseDurationDays(card.CardCode, out int durationDays);
            return new TimeCardItem(card.Id, card.CardCode, durationDays, card.IsUsed, card.UsedAt, card.UsedByAccountId, card.UsedByUsername);
        }).ToList();

        return Ok(new TimeCardListResponse(true, "查询成功。", items));
    }

    [HttpPost("time-cards/generate")]
    public ActionResult<GenerateTimeCardsResponse> GenerateTimeCards([FromBody] GenerateTimeCardsRequest request) {
        if (!IsAuthorized()) {
            return Unauthorized(GenerateTimeCardsResponse.FromError("请先登录后台。"));
        }

        if (!timeCardCodeService.IsSupportedDuration(request.DurationDays)) {
            return BadRequest(GenerateTimeCardsResponse.FromError("仅支持生成 1 天、7 天或 30 天卡密。"));
        }

        if (request.Quantity is < 1 or > 200) {
            return BadRequest(GenerateTimeCardsResponse.FromError("单次生成数量需在 1 到 200 之间。"));
        }

        using GameStorage.Request db = gameStorage.Context();

        try {
            var generatedCodes = new HashSet<string>(StringComparer.Ordinal);
            while (generatedCodes.Count < request.Quantity) {
                generatedCodes.Add(timeCardCodeService.Generate(request.DurationDays));
            }

            if (!db.CreateTimeCards(generatedCodes)) {
                return StatusCode(500, GenerateTimeCardsResponse.FromError("生成卡密失败，请稍后再试。"));
            }

            return Ok(new GenerateTimeCardsResponse(true, "卡密生成成功。", generatedCodes.OrderBy(code => code).ToList()));
        } catch (DbUpdateException ex) {
            Log.Warning(ex, "Failed to generate time cards with duration {DurationDays}", request.DurationDays);
            return StatusCode(500, GenerateTimeCardsResponse.FromError("生成卡密失败，请稍后再试。"));
        } catch (Exception ex) {
            Log.Error(ex, "Unexpected time-card generation failure");
            return StatusCode(500, GenerateTimeCardsResponse.FromError("生成卡密失败，请稍后再试。"));
        }
    }

    private bool IsAuthorized() {
        string? token = Request.Cookies[AdminSessionService.CookieName];
        if (!adminSessionService.ValidateSession(token)) {
            return false;
        }

        Response.Cookies.Append(AdminSessionService.CookieName, token!, BuildCookieOptions());
        return true;
    }

    private CookieOptions BuildCookieOptions() {
        return new CookieOptions {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            MaxAge = AdminSessionService.SessionLifetime,
            Expires = DateTimeOffset.UtcNow.Add(AdminSessionService.SessionLifetime),
            Path = "/",
        };
    }

    public sealed record ApiResponse(bool Success, string Message);

    public sealed record TimeCardItem(long Id, string CardCode, int DurationDays, bool IsUsed, DateTime? UsedAt, long? UsedByAccountId, string? UsedByUsername);

    public sealed record TimeCardListResponse(bool Success, string Message, IList<TimeCardItem> Items) {
        public static TimeCardListResponse FromError(string message) => new(false, message, []);
    }

    public sealed record GenerateTimeCardsResponse(bool Success, string Message, IList<string> CardCodes) {
        public static GenerateTimeCardsResponse FromError(string message) => new(false, message, []);
    }

    public sealed class AdminLoginRequest {
        public string Password { get; init; } = string.Empty;
    }

    public sealed class GenerateTimeCardsRequest {
        public int DurationDays { get; init; }
        public int Quantity { get; init; } = 1;
    }
}
