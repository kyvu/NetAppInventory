using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WebApp.IntegrationTests.UI;

/// <summary>
/// Example: press-and-hold "reveal" buttons on a paged grid of masked (password-type) inputs.
/// Catches two classic bugs: the reveal hitting the wrong row (duplicate ids / getElementById),
/// and the first press doing nothing on rows loaded by paging (handlers bound to icon elements).
/// </summary>
[TestFixture, Category("UI")]
public class MaskedFieldRevealTests : UiTestBase
{
    ILocator Toggles => Page.Locator(Sel("RevealToggle"));
    ILocator Fields => Page.Locator(Sel("MaskedField"));

    async Task OpenGrid()
    {
        await Page.GotoAsync(PageUrlOrIgnore("MaskedFieldGrid"));
        await Expect(Toggles.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Reveal_Only_Affects_Clicked_Row()
    {
        await OpenGrid();
        var rows = await Toggles.CountAsync();
        Assume.That(rows, Is.GreaterThanOrEqualTo(2), "Need at least 2 rows of test data.");

        var target = Math.Min(3, rows - 1);
        await Toggles.Nth(target).DispatchEventAsync("mousedown");

        await Expect(Fields.Nth(target)).ToHaveAttributeAsync("type", "text");
        for (int i = 0; i < rows; i++)
            if (i != target)
                await Expect(Fields.Nth(i)).ToHaveAttributeAsync("type", "password");

        await Toggles.Nth(target).DispatchEventAsync("mouseup");
        await Expect(Fields.Nth(target)).ToHaveAttributeAsync("type", "password");
    }

    [Test]
    public async Task Reveal_Works_On_First_Press_After_Paging()
    {
        await OpenGrid();

        var next = Page.Locator(Sel("NextPage"));
        if (await next.CountAsync() == 0) Assert.Ignore("Only one page of test data.");

        await next.First.ClickAsync();
        await Expect(Toggles.First).ToBeVisibleAsync();

        // Real mouse press, not a synthetic event
        await Toggles.First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Expect(Fields.First).ToHaveAttributeAsync("type", "text", new() { Timeout = 1000 });
        await Page.Mouse.UpAsync();
        await Expect(Fields.First).ToHaveAttributeAsync("type", "password");
    }

    [Test]
    public async Task No_Duplicate_Ids_On_Page()
    {
        await OpenGrid();
        var dupes = await Page.EvaluateAsync<string[]>(@"() => {
            const seen = {}, d = [];
            document.querySelectorAll('[id]').forEach(e => { if (seen[e.id]++) d.push(e.id); else seen[e.id] = 1; });
            return [...new Set(d)];
        }");
        Assert.That(dupes, Is.Empty, "Duplicate element ids: " + string.Join(", ", dupes));
    }
}
