using HauntedVoiceUniverse.Modules.Wallet.Models;

namespace HauntedVoiceUniverse.Modules.Wallet.Services;

internal sealed class AppreciationStickerDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Language { get; init; } = "";
    public int CoinAmount { get; init; }
    public string ImageUrl { get; init; } = "";
    public int DisplayOrder { get; init; }
}

internal static class AppreciationStickerCatalog
{
    private static readonly IReadOnlyList<AppreciationStickerDefinition> _all = new List<AppreciationStickerDefinition>
    {
        new() { Id = "jinn-eye-circle", Name = "Jinn Eye", Language = "hindi", CoinAmount = 40, ImageUrl = "/appreciation-stickers/h1-jinn-eye-circle.svg", DisplayOrder = 1 },
        new() { Id = "skull-rounded-square", Name = "Skull Praise", Language = "hindi", CoinAmount = 60, ImageUrl = "/appreciation-stickers/h2-skull-rounded-square.svg", DisplayOrder = 2 },
        new() { Id = "candle-glow", Name = "Candle Glow", Language = "hindi", CoinAmount = 50, ImageUrl = "/appreciation-stickers/h3-candle.svg", DisplayOrder = 3 },
        new() { Id = "moon-bat", Name = "Moon Bat", Language = "hindi", CoinAmount = 80, ImageUrl = "/appreciation-stickers/h4-moon-bat.svg", DisplayOrder = 4 },
        new() { Id = "flame-heart", Name = "Flame Heart", Language = "hindi", CoinAmount = 90, ImageUrl = "/appreciation-stickers/h5-flame-heart.svg", DisplayOrder = 5 },
        new() { Id = "spider-web", Name = "Spider Web", Language = "hinglish", CoinAmount = 30, ImageUrl = "/appreciation-stickers/hl1-spider-web.svg", DisplayOrder = 6 },
        new() { Id = "ghost-cheer", Name = "Ghost Cheer", Language = "hinglish", CoinAmount = 70, ImageUrl = "/appreciation-stickers/hl2-ghost.svg", DisplayOrder = 7 },
        new() { Id = "cracked-screen", Name = "Cracked Screen", Language = "hinglish", CoinAmount = 100, ImageUrl = "/appreciation-stickers/hl3-cracked-screen.svg", DisplayOrder = 8 },
        new() { Id = "blood-drop", Name = "Blood Drop", Language = "hinglish", CoinAmount = 110, ImageUrl = "/appreciation-stickers/hl4-blood-drop.svg", DisplayOrder = 9 },
        new() { Id = "knife-slash", Name = "Knife Slash", Language = "hinglish", CoinAmount = 120, ImageUrl = "/appreciation-stickers/hl5-knife.svg", DisplayOrder = 10 },
        new() { Id = "haunted-window", Name = "Haunted Window", Language = "english", CoinAmount = 130, ImageUrl = "/appreciation-stickers/e1-haunted-window.svg", DisplayOrder = 11 },
        new() { Id = "raven-crow", Name = "Raven Crow", Language = "english", CoinAmount = 140, ImageUrl = "/appreciation-stickers/e2-raven-crow.svg", DisplayOrder = 12 },
        new() { Id = "horror-award-ribbon", Name = "Horror Ribbon", Language = "english", CoinAmount = 150, ImageUrl = "/appreciation-stickers/e3-horror-award-ribbon.svg", DisplayOrder = 13 },
        new() { Id = "broken-heart-stitches", Name = "Broken Heart", Language = "english", CoinAmount = 160, ImageUrl = "/appreciation-stickers/e4-broken-heart-stitches.svg", DisplayOrder = 14 },
        new() { Id = "monster-claw", Name = "Monster Claw", Language = "english", CoinAmount = 180, ImageUrl = "/appreciation-stickers/e5-monster-claw.svg", DisplayOrder = 15 },
    };

    public static List<AppreciationStickerResponse> ToResponses()
    {
        return _all
            .Select(sticker => new AppreciationStickerResponse
            {
                Id = sticker.Id,
                Name = sticker.Name,
                Language = sticker.Language,
                CoinAmount = sticker.CoinAmount,
                ImageUrl = sticker.ImageUrl,
                DisplayOrder = sticker.DisplayOrder
            })
            .ToList();
    }

    public static AppreciationStickerDefinition? Find(string? stickerId)
    {
        if (string.IsNullOrWhiteSpace(stickerId)) return null;
        return _all.FirstOrDefault(sticker =>
            string.Equals(sticker.Id, stickerId, StringComparison.OrdinalIgnoreCase));
    }
}
