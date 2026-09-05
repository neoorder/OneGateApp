using NeoOrder.OneGate.Pages;
using Xunit;

public class MnemonicSelectionTests
{
    [Fact]
    public void Deselect_removes_the_whole_word_and_reselection_restores_it()
    {
        var page = NewPage();
        var word = new Button { Text = "abandon" };
        page.SelectForTest(word);
        Assert.Equal("abandon", page.PhraseForTest);
        page.SelectForTest(word);
        Assert.Equal("", page.PhraseForTest);
        Assert.Equal(1, word.Opacity);
        page.SelectForTest(word);
        Assert.Equal("abandon", page.PhraseForTest);
    }

    [Fact]
    public void Deselecting_a_middle_word_preserves_other_whole_words()
    {
        var page = NewPage();
        var first = new Button { Text = "abandon" };
        var middle = new Button { Text = "ability" };
        var last = new Button { Text = "able" };
        page.SelectForTest(first);
        page.SelectForTest(middle);
        page.SelectForTest(last);
        page.SelectForTest(middle);
        Assert.Equal("abandon able", page.PhraseForTest);
        page.SelectForTest(middle);
        Assert.Equal("abandon able ability", page.PhraseForTest);
    }

    [Fact]
    public void Equal_words_are_independent_instances_in_the_selected_order()
    {
        var page = NewPage();
        var first = new Button { Text = "abandon" };
        var middle = new Button { Text = "ability" };
        var last = new Button { Text = "abandon" };
        page.SelectForTest(first);
        page.SelectForTest(middle);
        page.SelectForTest(last);
        page.SelectForTest(first);
        Assert.Equal("ability abandon", page.PhraseForTest);
        Assert.Equal(1, first.Opacity);
        Assert.Equal(0.1, last.Opacity);
    }

    [Fact]
    public void Complete_phrase_keeps_every_word_in_the_selected_order()
    {
        var page = NewPage();
        string[] words = ["abandon", "ability", "able", "about", "above", "absent", "absorb", "abstract", "absurd", "abuse", "access", "accident"];
        foreach (string word in words) page.SelectForTest(new Button { Text = word });
        Assert.Equal(string.Join(" ", words), page.PhraseForTest);
    }

    static VerifyMnemonicPage NewPage() => new(new EmptyServices(), new ScreenSecurity());
}
