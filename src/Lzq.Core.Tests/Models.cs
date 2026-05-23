namespace Lzq.Core.Tests;

public class User
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string Email { get; set; }
    public Address Address { get; set; }
    public string Password { get; set; }
}

public class UserDto
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string Email { get; set; }
    public string City { get; set; } // 来自 Address.City
    public object Password { get; set; }
}

public class Address
{
    public string City { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public string Product { get; set; }
}

public class Customer
{
    public string CustomerName { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public string Product { get; set; }
    public string CustomerName { get; set; }
}

public class CopyTest
{
    public string Value { get; set; }
    public List<int> Numbers { get; set; }
}