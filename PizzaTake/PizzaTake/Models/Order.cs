using SQLite;

namespace PizzaTake.Models;

[Table("Orders")]
public class Order
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int PizzeriaId { get; set; }
    public OrderStatus Status { get; set; }
    public DeliveryType DeliveryType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string DeliveryAddress { get; set; } = string.Empty;
    public decimal DeliveryFee { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
