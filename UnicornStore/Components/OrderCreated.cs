using System;

namespace UnicornStore.Components
{
    public class OrderCreated
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal Total { get; set; }
    }
}
