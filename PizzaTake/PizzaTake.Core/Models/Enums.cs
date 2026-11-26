namespace PizzaTake.Core.Models;

public enum DeliveryType
{
    Delivery,
    Pickup
}

public enum OrderStatus
{
    Pending,
    Accepted,
    Preparing,
    Ready,
    Delivering,
    Completed,
    Cancelled
}

public enum PaymentMethod
{
    Cash,
    CardOnline,
    CardOnDelivery
}

public enum OptionKind
{
    AddOn,
    Exclusion,
    Replacement
}

public enum UserRole
{
    Customer,
    Administrator
}
