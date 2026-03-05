namespace HauntedVoiceUniverse.Common;

public static class UserRoles
{
    public const string Reader = "reader";
    public const string Creator = "creator";
    public const string Moderator = "moderator";
    public const string FinanceManager = "finance_manager";
    public const string SupportAgent = "support_agent";
    public const string ContentReviewer = "content_reviewer";
    public const string SuperAdmin = "super_admin";

    public static readonly string[] AdminRoles = 
        { Moderator, FinanceManager, SupportAgent, ContentReviewer, SuperAdmin };
}

public static class CreatorRanks
{
    public const string PretAatma = "pret_aatma";
    public const string ShraapitLekhak = "shraapit_lekhak";
    public const string AndhkaarRachnakar = "andhkaar_rachnakar";
    public const string BhootSamrat = "bhoot_samrat";
    public const string TantrikMaster = "tantrik_master";
    public const string MahaKaalKathaSamrat = "mahakaal_katha_samrat";
}

public static class ReaderRanks
{
    public const string RaatKaMusafir = "raat_ka_musafir";
    public const string AndheriGaliExplorer = "andheri_gali_explorer";
    public const string ShamshaanPremi = "shamshaan_premi";
    public const string HorrorBhakt = "horror_bhakt";
    public const string MahaKaalBhakt = "mahakaal_bhakt";
}

public static class RechargePacksInRupees
{
    public static readonly int[] Packs = { 10, 49, 99, 199, 499 };
}

// ✅ HvuClaims naam rakha - System.Security.Claims.ClaimTypes se conflict avoid karne ke liye
public static class HvuClaims
{
    public const string UserId = "uid";
    public const string Role = "role";
    public const string Email = "email";
    public const string Username = "username";
    public const string IsCreator = "is_creator";
}

public static class CacheKeys
{
    public const string GlobalSettings = "global_settings";
    public const string Categories = "categories";
    public const string Tags = "tags";
    public static string UserProfile(Guid id) => $"user_profile_{id}";
    public static string StoryDetail(Guid id) => $"story_{id}";
    public static string Leaderboard(string type) => $"leaderboard_{type}";
}