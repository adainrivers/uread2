using URead2.Profiles.Abstractions;
using URead2.Profiles.Engine;
using URead2.Profiles.Games.DuneAwakening;

namespace URead2;

/// <summary>
/// Factory for creating game profiles.
/// </summary>
public static class ProfilesFactory
{
    /// <summary>
    /// Base Unreal Engine profile. Works for most UE4/UE5 games.
    /// </summary>
    public static IProfile UE_Base() => new UE_BaseProfile();

    /// <summary>
    /// Profile for Dune Awakening.
    /// </summary>
    public static IProfile DuneAwakening() => new DuneAwakeningProfile();

    /// <summary>
    /// Initializes Oodle decompression for the profile.
    /// </summary>
    public static IProfile WithOodleDecompression(this IProfile profile, string oodleDllPath)
    {
        profile.Decompressor.InitializeOodle(oodleDllPath);
        return profile;
    }

    /// <summary>
    /// Initializes zlib-ng decompression for the profile.
    /// </summary>
    public static IProfile WithZlibDecompression(this IProfile profile, string zlibDllPath)
    {
        profile.Decompressor.InitializeZlibNg(zlibDllPath);
        return profile;
    }
}
