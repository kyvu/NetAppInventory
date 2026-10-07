using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WebApp.IntegrationTests.UI;

/// <summary>
/// Example: a Bootstrap modal containing a grid + "add" form. After Save the grid should refresh
/// inside the modal, the modal should stay open, and no JS errors should be thrown
/// (e.g. "Cannot reinitialise DataTable"). Writes one record – run against test data only.
/// </summary>
[TestFixture, Category("UI")]
public class ModalFormTests : UiTestBase
{
    [Test]
    public async Task Save_Keeps_Modal_Open_And_Refreshes_Grid()
    {
        await Page.GotoAsync(PageUrlOrIgnore("ModalFormPage"));

        await Page.Locator(Sel("ModalOpen")).First.ClickAsync();
        var modal = Page.Locator(Sel("Modal"));
        await Expect(modal).ToBeVisibleAsync();

        var grid = modal.Locator(Sel("ModalGrid")).First;
        await Expect(grid).ToBeVisibleAsync();

        var add = modal.Locator(Sel("ModalAdd"));
        if (await add.CountAsync() == 0) Assert.Ignore("Add button not shown on this page.");
        await add.First.ClickAsync();

        var text = $"Automated test {DateTime.Now:yyyyMMdd-HHmmss}";
        await modal.Locator(Sel("ModalText")).First.FillAsync(text);
        await modal.Locator(Sel("ModalSave")).First.ClickAsync();

        await Expect(modal).ToBeVisibleAsync();
        await Expect(modal.Locator(Sel("ModalGrid")).First).ToContainTextAsync(text);
        Assert.That(ConsoleErrors, Is.Empty, "JS errors: " + string.Join(" | ", ConsoleErrors));
    }
}
