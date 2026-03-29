using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Microsoft.Extensions.Hosting;

namespace Maple2.Server.Game;

public class AccountExpiryMonitorService(GameServer server, GameStorage gameStorage) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(Constant.AccountExpiryCheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            List<GameSession> sessions = server.GetSessions()
                .Where(session => session.AccountId > 0)
                .ToList();
            if (sessions.Count == 0) {
                continue;
            }

            using GameStorage.Request db = gameStorage.Context();
            DateTime nowUtc = DateTime.UtcNow;
            Dictionary<long, DateTime> expireLookup = db.GetAccountExpireAtLookup(sessions.Select(session => session.AccountId));

            foreach (GameSession session in sessions) {
                if (!expireLookup.TryGetValue(session.AccountId, out DateTime expireAt) || expireAt <= nowUtc) {
                    session.Send(NoticePacket.Disconnect(new InterfaceText(Constant.AccountExpiredMessage)));
                    session.Disconnect();
                }
            }
        }
    }
}
