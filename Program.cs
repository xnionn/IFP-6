using System;
using System.Globalization;

Console.WriteLine("=== Original mutable workflow: mistakes still compile ===");

var twiceDiscounted = new LegacyCheckout();
twiceDiscounted.Start(1000m);
twiceDiscounted.Validate();
twiceDiscounted.ApplyDiscount(0.10m);
twiceDiscounted.ApplyDiscount(0.10m); // Wrong: applies 10% twice.
twiceDiscounted.Charge();

var discountForgotten = new LegacyCheckout();
discountForgotten.Start(1000m);
discountForgotten.Validate();
discountForgotten.Charge(); // Wrong: discount was skipped.

var chargedTwice = new LegacyCheckout();
chargedTwice.Start(1000m);
chargedTwice.Validate();
chargedTwice.ApplyDiscount(0.10m);
chargedTwice.Charge();
chargedTwice.Charge(); // Wrong: this performs the side effect twice.

var chargedBeforeValidation = new LegacyCheckout();
chargedBeforeValidation.Start(1000m);
try
{
    chargedBeforeValidation.Charge();
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Caught before-validation failure: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Staged immutable workflow ===");

var draft = new Draft(1000m);
var validated = CheckoutWorkflow.Validate(draft);
var priced = CheckoutWorkflow.ApplyDiscount(validated, 0.10m);

Console.WriteLine("Real gateway:");
var realReceipt = CheckoutWorkflow.Charge(priced, RealGateway);
Console.WriteLine($"Receipt: {realReceipt.TransactionId}, {realReceipt.Amount.ToString("F2", CultureInfo.InvariantCulture)}");

Console.WriteLine("Fake gateway:");
var fakeCalls = 0;
Func<decimal, string> fakeGateway = amount =>
{
    fakeCalls++;
    Console.WriteLine($"Fake gateway received {amount.ToString("F2", CultureInfo.InvariantCulture)}");
    return $"fake-{fakeCalls}";
};
var fakeReceipt = CheckoutWorkflow.Charge(priced, fakeGateway);
Console.WriteLine($"Receipt: {fakeReceipt.TransactionId}, {fakeReceipt.Amount.ToString("F2", CultureInfo.InvariantCulture)}; fake calls: {fakeCalls}");

// These are intentionally invalid staged calls. Uncomment one at a time to see the compiler error.
// Discount twice: CS1503 — ApplyDiscount expects Validated, but receives Priced.
// var discountedTwice = CheckoutWorkflow.ApplyDiscount(CheckoutWorkflow.ApplyDiscount(validated, 0.10m), 0.10m);

// Forget discount: CS1503 — Charge expects Priced, but receives Validated.
// var withoutDiscount = CheckoutWorkflow.Charge(validated, RealGateway);

// Charge twice: the compiler ACCEPTS this; a Priced value can be reused, so it invokes the gateway twice.
// var firstReceipt = CheckoutWorkflow.Charge(priced, RealGateway);
// var secondReceipt = CheckoutWorkflow.Charge(priced, RealGateway);

// Charge before validation: CS1503 — Charge expects Priced, but receives Draft.
// var prematureReceipt = CheckoutWorkflow.Charge(draft, RealGateway);

static string RealGateway(decimal amount)
{
    Console.WriteLine($"Real gateway: charging {amount.ToString("F2", CultureInfo.InvariantCulture)}");
    return $"real-{Guid.NewGuid():N}";
}

public sealed class LegacyCheckout
{
    private decimal _total;
    private bool _validated;

    public void Start(decimal amount) => _total = amount;
    public void Validate() => _validated = _total > 0;
    public void ApplyDiscount(decimal percent) => _total -= _total * percent;

    public void Charge()
    {
        if (!_validated)
            throw new InvalidOperationException("Not validated.");

        Console.WriteLine($"Charged {_total.ToString("F2", CultureInfo.InvariantCulture)}");
    }
}

public sealed record Draft(decimal Amount);
public sealed record Validated(decimal Amount);
public sealed record Priced(decimal Amount);
public sealed record Receipt(string TransactionId, decimal Amount);

public static class CheckoutWorkflow
{
    public static Validated Validate(Draft draft)
    {
        if (draft.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(draft), "Amount must be positive.");

        return new Validated(draft.Amount);
    }

    public static Priced ApplyDiscount(Validated validated, decimal percent)
    {
        if (percent is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(percent), "Discount must be between 0 and 1.");

        return new Priced(validated.Amount - validated.Amount * percent);
    }

    public static Receipt Charge(Priced priced, Func<decimal, string> gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        var transactionId = gateway(priced.Amount);
        return new Receipt(transactionId, priced.Amount);
    }
}
