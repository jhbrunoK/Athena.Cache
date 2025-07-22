namespace Athena.Cache.Sample.Models;

public class OrderDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime OrderDate { get; set; }
}