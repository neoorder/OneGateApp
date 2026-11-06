using System.Globalization;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Services;
using Xunit;

public class PaymentActionTests
{
    [Fact]
    public void Protocol_amount_at_character_limit_is_preserved()
    {
        string amount = new string('0', 1023) + "1";
        var action = Assert.IsType<PaymentAction>(PaymentAction.TryCreate("neo:synthetic-recipient?amount=" + amount));
        Assert.Equal(amount, action.Amount);
    }

    [Theory]
    [InlineData(1025)]
    [InlineData(16384)]
    public void Overlong_protocol_amount_is_rejected(int length)
    {
        string amount = new string('0', length - 1) + "1";
        Assert.Null(PaymentAction.TryCreate("neo:synthetic-recipient?amount=" + amount));
        Assert.Null(AppLinkAction.TryCreate("neo:synthetic-recipient?amount=" + amount));
    }

    [Theory]
    [InlineData("en-US", "1.5")]
    [InlineData("de-DE", "1.5")]
    [InlineData("fr-FR", "1.5")]
    [InlineData("de-DE", "123456789012345678901234567890.12345678")]
    [InlineData("fr-FR", "0.000000000000000000000000000000000000000000000000000000000001")]
    public async Task Payment_uri_preserves_protocol_digits_until_send_page(string locale, string amount)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new(locale);
            var action = Assert.IsType<PaymentAction>(AppLinkAction.TryCreate("neo:synthetic-recipient?asset=gas&amount=" + amount));
            var shell = new Shell();
            await action.GotoRoute(shell);
            string protocolAmount = Assert.IsType<string>(shell.Query!["amount"]);
            Assert.Equal(amount, protocolAmount);
            string localized = protocolAmount.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            Assert.True(TokenAmount.TryParse(protocolAmount, 60, out var expected, CultureInfo.InvariantCulture));
            Assert.True(TokenAmount.TryParse(localized, 60, out var actual));
            Assert.Equal(expected, actual);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000.5")]
    [InlineData("1e3")]
    [InlineData("-1")]
    [InlineData("+")]
    [InlineData(" ")]
    public void Protocol_amount_rejects_localized_grouping_and_ambiguous_input(string amount)
        => Assert.Null(PaymentAction.TryCreate("neo:synthetic-recipient?amount=" + Uri.EscapeDataString(amount)));

    [Fact]
    public async Task Payment_without_amount_does_not_invent_one()
    {
        var action = Assert.IsType<PaymentAction>(PaymentAction.TryCreate("neo:synthetic-recipient?asset=neo"));
        var shell = new Shell();
        await action.GotoRoute(shell);
        Assert.False(shell.Query!.ContainsKey("amount"));
    }
}
