using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// Every case here is a real tag from the library, with what pixiv answered
/// for it when asked for Traditional Chinese.
/// </summary>
public class TagTranslationPolicyTests
{
    [Theory]
    [InlineData("コイカツ!", "戀活！", "戀活！")]
    [InlineData("触手", "觸手", "觸手")]
    [InlineData("うごイラ", "動圖", "動圖")]
    // pixiv has no Chinese and answered in English: the kanji tag is shown.
    [InlineData("原神", "Genshin Impact", "原神")]
    [InlineData("巨乳", "large breasts", "巨乳")]
    // Kana: still the original, not the English.
    [InlineData("乗り物", "vehicle", "乗り物")]
    [InlineData("珊瑚宮心海", "Sangonomiya Kokomi", "珊瑚宮心海")]
    // Latin tags keep their own spelling rather than an English expansion.
    [InlineData("NTR", "cuckold", "NTR")]
    [InlineData("FGO", "Fate/Grand Order", "FGO")]
    // No translation at all.
    [InlineData("シーン配布(コイカツ!)", null, "シーン配布(コイカツ!)")]
    public void AChineseReaderGetsChineseOrTheOriginalNeverEnglish(string tag, string? translation, string expected)
    {
        Assert.Equal(expected, TagTranslationPolicy.Label("zh-Hant", tag, translation));
        Assert.Equal(expected, TagTranslationPolicy.Label("zh-Hans", tag, translation));
    }

    [Theory]
    [InlineData("原神", "Genshin Impact", "Genshin Impact")]
    [InlineData("乗り物", "vehicle", "vehicle")]
    [InlineData("シーン配布(コイカツ!)", null, "シーン配布(コイカツ!)")]
    public void AnEnglishReaderGetsEnglishThenTheOriginal(string tag, string? translation, string expected)
        => Assert.Equal(expected, TagTranslationPolicy.Label("en-US", tag, translation));

    [Fact]
    public void TheFirstUsableCandidateWinsSoAChineseSidecarBeatsAnEnglishDictionaryAnswer()
        => Assert.Equal("原神", TagTranslationPolicy.Label("zh-Hant", "Genshin", "Genshin Impact", "原神"));

    [Fact]
    public void ATranslationThatIsJustTheTagAddsNothing()
        => Assert.Null(TagTranslationPolicy.Pick("zh-Hant", "原神", "原神"));
}
