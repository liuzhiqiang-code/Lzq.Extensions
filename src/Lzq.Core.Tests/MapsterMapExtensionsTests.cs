//using Mapster;
//using Xunit;

//namespace Lzq.Core.Tests;

//public class MapsterMapExtensionsTests
//{
//    // 1. 基础映射
//    [Fact]
//    public void Map_SimpleObject_ReturnsMapped()
//    {
//        var user = new User { Name = "Tom", Age = 20, Email = "tom@test.com" };
//        var dto = user.Map<UserDto>();
//        Assert.Equal("Tom", dto.Name);
//        Assert.Equal(20, dto.Age);
//    }

//    // 2. 使用自定义 TypeAdapterConfig 映射
//    [Fact]
//    public void Map_WithConfig_AppliesRule()
//    {
//        var user = new User { Name = "Jerry", Age = 30, Email = "jerry@test.com" };
//        var config = new TypeAdapterConfig();
//        config.NewConfig<User, UserDto>().Map(dest => dest.City, src => "Shanghai");

//        var dto = user.Map<UserDto>(config);
//        Assert.Equal("Shanghai", dto.City);
//    }

//    // 3. 忽略源中 null 值
//    [Fact]
//    public void MapIgnoreNull_SkipsNullSourceValues()
//    {
//        var user = new User { Name = null, Age = 25, Email = "test@test.com" };
//        var result = user.MapIgnoreNull<UserDto>();

//        // null 源值被忽略，目标属性保持默认值（null）
//        Assert.Null(result.Name);
//        Assert.Equal(25, result.Age);
//        Assert.Equal("test@test.com", result.Email);
//    }

//    // 4. 映射并忽略指定属性
//    [Fact]
//    public void MapIgnore_SkipsSpecifiedProperties()
//    {
//        var user = new User
//        {
//            Name = "Alice",
//            Age = 28,
//            Email = "alice@test.com",
//            Password = "secret"
//        };

//        // 忽略 Password 和 Email 属性
//        var dto = user.MapIgnore<User, UserDto>(
//            x => x.Password,
//            x => x.Email);

//        Assert.Equal("Alice", dto.Name);
//        Assert.Equal(28, dto.Age);
//        // 被忽略的属性保持默认值
//        Assert.Null(dto.Password);
//        Assert.Null(dto.Email);
//    }

//    // 5. 自定义规则临时映射
//    [Fact]
//    public void MapWith_CustomRule_AppliesTemporaryConfig()
//    {
//        var user = new User { Name = "Sam", Age = 40, Email = "sam@test.com" };

//        var dto = user.MapWith<User, UserDto>(cfg =>
//            cfg.Map(dest => dest.City, src => "FromMapWith"));

//        Assert.Equal("FromMapWith", dto.City);
//        Assert.Equal("Sam", dto.Name);
//    }

//    // 6. MapWith 结合忽略 null 值
//    [Fact]
//    public void MapWith_IgnoreNullValues_SkipsNullsAndAppliesRule()
//    {
//        var user = new User { Name = null, Age = 33, Email = "null@test.com" };

//        var dto = user.MapWith<User, UserDto>(
//            cfg => cfg.Map(dest => dest.City, src => "Beijing"),
//            ignoreNullValues: true);

//        // 忽略 null 值：Name 不会被映射，保留默认值 null
//        Assert.Null(dto.Name);
//        Assert.Equal(33, dto.Age);
//        Assert.Equal("Beijing", dto.City);
//    }

//    // 7. 深拷贝
//    [Fact]
//    public void DeepCopy_CreatesIndependentClone()
//    {
//        var original = new CopyTest
//        {
//            Value = "Original",
//            Numbers = new List<int> { 1, 2, 3 }
//        };

//        var clone = original.DeepCopy();
//        Assert.Equal(original.Value, clone.Value);
//        Assert.Equal(original.Numbers, clone.Numbers);
//        Assert.NotSame(original.Numbers, clone.Numbers);
//    }
//}