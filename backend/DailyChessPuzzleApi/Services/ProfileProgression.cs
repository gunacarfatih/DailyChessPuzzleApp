using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Services;

public static class ProfileProgression
{
    private sealed record Metrics(int Daily, int Bonus, int Perfect, int BestStreak,
        int Xp, int Hard, int NoHint, int NoWrong);
    private sealed record Achievement(string Id, string Title, string Description,
        Func<Metrics, int> Current, int Goal);

    private static readonly Achievement[] Catalog =
    {
        new("first_solve", "İlk Adım", "İlk bulmacanı çöz.", m=>m.Daily+m.Bonus, 1),
        new("daily_10", "Alışkanlık", "10 günlük bulmaca çöz.", m=>m.Daily, 10),
        new("daily_50", "Düzenli Oyuncu", "50 günlük bulmaca çöz.", m=>m.Daily, 50),
        new("daily_100", "Yüzüncü Zafer", "100 günlük bulmaca çöz.", m=>m.Daily, 100),
        new("daily_365", "Her Gün Tahta Başında", "365 günlük bulmaca çöz.", m=>m.Daily, 365),
        new("streak_3", "Üç Günlük Seri", "3 günlük seri yap.", m=>m.BestStreak, 3),
        new("streak_7", "Haftalık Seri", "7 günlük seri yap.", m=>m.BestStreak, 7),
        new("streak_30", "Aylık Seri", "30 günlük seri yap.", m=>m.BestStreak, 30),
        new("streak_100", "Demir Disiplin", "100 günlük seri yap.", m=>m.BestStreak, 100),
        new("bonus_1", "Ekstra Tur", "İlk bonus bulmacanı çöz.", m=>m.Bonus, 1),
        new("bonus_10", "Bonus Avcısı", "10 bonus bulmaca çöz.", m=>m.Bonus, 10),
        new("bonus_50", "Bonus Ustası", "50 bonus bulmaca çöz.", m=>m.Bonus, 50),
        new("perfect_1", "Temiz Çözüm", "Hata ve ipucu olmadan çöz.", m=>m.Perfect, 1),
        new("perfect_10", "Keskin Görüş", "10 kusursuz çözüm yap.", m=>m.Perfect, 10),
        new("perfect_50", "Hatasız Usta", "50 kusursuz çözüm yap.", m=>m.Perfect, 50),
        new("solves_25", "Taktik Öğrencisi", "25 bulmaca çöz.", m=>m.Daily+m.Bonus, 25),
        new("solves_250", "Taktik Uzmanı", "250 bulmaca çöz.", m=>m.Daily+m.Bonus, 250),
        new("solves_1000", "Satranç Makinesi", "1000 bulmaca çöz.", m=>m.Daily+m.Bonus, 1000),
        new("xp_500", "İlk Birikim", "500 XP kazan.", m=>m.Xp, 500),
        new("xp_2500", "Tecrübe", "2500 XP kazan.", m=>m.Xp, 2500),
        new("xp_10000", "Kıdemli", "10000 XP kazan.", m=>m.Xp, 10000),
        new("xp_50000", "Efsane", "50000 XP kazan.", m=>m.Xp, 50000),
        new("hard_1", "Zorlu Mücadele", "2000+ rating bulmacayı çöz.", m=>m.Hard, 1),
        new("hard_10", "Zorluk Tanımaz", "10 zor bulmaca çöz.", m=>m.Hard, 10),
        new("hard_50", "Usta Taktikçi", "50 zor bulmaca çöz.", m=>m.Hard, 50),
        new("no_hint_25", "Kendi Sezgine Güven", "25 bulmacayı ipucusuz çöz.", m=>m.NoHint, 25),
        new("no_wrong_25", "Soğukkanlı", "25 bulmacayı hatasız çöz.", m=>m.NoWrong, 25),
        new("mixed_10", "Çok Yönlü", "10 bonus ve 10 günlük bulmaca çöz.", m=>Math.Min(m.Daily,m.Bonus), 10),
        new("streak_14", "İki Haftalık Seri", "14 günlük seri yap.", m=>m.BestStreak, 14),
        new("streak_60", "Tahtanın Daimîsi", "60 günlük seri yap.", m=>m.BestStreak, 60),
    };

    public static int DifficultyBonus(int rating) => rating >= 2000 ? 50 : rating >= 1400 ? 25 : 0;
    public static int AwardXp(int difficultyBonus, int wrongMoves, bool usedHint) =>
        100 + difficultyBonus + (wrongMoves == 0 ? 25 : 0) + (!usedHint ? 25 : 0);

    public static int LevelFor(int xp) => Math.Max(1, (int)Math.Sqrt(Math.Max(0, xp) / 100d) + 1);
    public static string RankFor(int level) => level switch
    {
        >= 50 => "Grandmaster", >= 35 => "Master", >= 25 => "Diamond",
        >= 15 => "Platinum", >= 8 => "Gold", >= 4 => "Silver", _ => "Bronze"
    };

    public static async Task AwardAsync(ApplicationDbContext db, int userId, PuzzleAttempt attempt,
        string kind, int difficultyBonus, int wrongMoves, bool usedHint, CancellationToken ct)
    {
        db.ProfileProgress.Add(new ProfileProgress
        {
            UserId = userId, PuzzleAttemptId = attempt.Id, Kind = kind,
            Xp = AwardXp(difficultyBonus, wrongMoves, usedHint), DifficultyBonus = difficultyBonus,
            WrongMoves = wrongMoves, UsedHint = usedHint, PlayedOn = attempt.PlayedOn,
            CreatedAtUtc = attempt.SubmittedAtUtc
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<object> GetAsync(ApplicationDbContext db, int userId, CancellationToken ct)
    {
        var rows = await db.ProfileProgress.AsNoTracking().Include(x => x.PuzzleAttempt)
            .Where(x => x.UserId == userId).ToListAsync(ct);
        var daily = rows.Where(x => x.Kind == "daily").ToArray();
        var bonus = rows.Where(x => x.Kind == "bonus").ToArray();
        var dates = daily.Select(x => x.PlayedOn).Distinct().Order().ToArray();
        var best = 0; var run = 0; DateOnly? prior = null;
        foreach (var date in dates) { run = prior == date.AddDays(-1) ? run + 1 : 1; best = Math.Max(best, run); prior = date; }
        var current = 0;
        if (dates.Length > 0 && dates[^1] >= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1))
            for (var i = dates.Length - 1; i >= 0 && (i == dates.Length - 1 || dates[i] == dates[i + 1].AddDays(-1)); i--) current++;
        var milestoneXp = (best >= 7 ? 100 : 0) + (best >= 30 ? 500 : 0);
        var xp = rows.Sum(x => x.Xp) + milestoneXp;
        var solved = daily.Length + bonus.Length;
        var perfect = rows.Count(x => x.WrongMoves == 0 && !x.UsedHint);
        var hard = rows.Count(x => x.DifficultyBonus == 50);
        var noHint = rows.Count(x => !x.UsedHint);
        var noWrong = rows.Count(x => x.WrongMoves == 0);
        var metrics = new Metrics(daily.Length, bonus.Length, perfect, best, xp, hard, noHint, noWrong);
        var achievements = Catalog.Select(a => {
            var value = a.Current(metrics);
            return new { id = a.Id, title = a.Title, description = a.Description,
                unlocked = value >= a.Goal, progress = Math.Min(value, a.Goal), goal = a.Goal };
        }).ToArray();
        var level = LevelFor(xp);
        return new { xp, level, rank = RankFor(level),
            currentStreak = current, bestStreak = best, solved, dailySolved = daily.Length,
            bonusSolved = bonus.Length, perfect, noHint, noWrong, hardSolved = hard,
            averageSeconds = solved == 0 ? 0 : (int)Math.Round(rows.Average(x => x.PuzzleAttempt.Seconds)),
            averageWrongMoves = solved == 0 ? 0 : Math.Round(rows.Average(x => x.WrongMoves), 1),
            levelProgress = (xp - (level - 1) * (level - 1) * 100) / (double)(level * level * 100 - (level - 1) * (level - 1) * 100),
            achievements, achievementCount = Catalog.Length };
    }
}
