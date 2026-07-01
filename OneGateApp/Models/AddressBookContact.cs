using Neo;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Models;

record AddressBookContact
{
    public required UInt160 Hash { get; init; }
    public required string Address { get; init; }
    public required string Label { get; init; }
    public string Source { get; init; } = "addressBook";

    public static AddressBookContact From(Contact contact, UInt160 hash)
    {
        return new AddressBookContact
        {
            Hash = hash,
            Address = contact.Address,
            Label = contact.Label
        };
    }
}
