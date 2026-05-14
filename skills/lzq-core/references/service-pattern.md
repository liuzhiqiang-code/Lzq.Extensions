# ServiceBase 模式 — 完整参考

## 最小配置

```csharp
public class XxxService : ServiceBase
{
    public XxxService() : base("/api/v1/xxx") { }
    
    private ISqlSugarClient DB => GetRequiredService<ISqlSugarClient>();
    private ILogger<XxxService> Logger => GetRequiredService<ILogger<XxxService>>();
    private ICurrentUser CurrentUser => GetRequiredService<ICurrentUser>();
}
```

## RoutePattern 参数

`[RoutePattern(pattern: "xxx", isAutoMapping: true, HttpMethod = "Post")]`

- `isAutoMapping: true` → 自动将方法映射为路由，前缀由构造函数提供。
- `HttpMethod` 默认 `Post`，可指定 `Get`、`Put`、`Delete`。
- 如果路由需要路径参数，在 pattern 中使用占位符，如 `delete/{id}`。

## 端点模式

### 分页查询

``` csharp
[RoutePattern(pattern: "page", true)]
public async Task<ApiResult> PageAsync([FromBody] XxxPageRequest req)
{
    var query = DB.Queryable<XxxEntity>();
    if (!string.IsNullOrEmpty(req.Keyword))
        query = query.Where(x => x.Name.Contains(req.Keyword));
    var total = await query.CountAsync();
    var items = await query.Skip((req.Page - 1) * req.PageSize)
                           .Take(req.PageSize).ToListAsync();
    return ApiResult.Success(new PagedResponse<XxxViewDto>(
        items.Map<List<XxxViewDto>>(), total));
}
```

### 详情查询

``` csharp
[RoutePattern(pattern: "detail", true)]
public async Task<ApiResult> DetailAsync(long id)
{
    var entity = await DB.Queryable<XxxEntity>().FirstAsync(x => x.Id == id);
    return entity == null
        ? ApiResult.Fail("记录不存在", 404)
        : ApiResult.Success(entity.Map<XxxViewDto>());
}
```

### 创建

``` csharp
[RoutePattern(pattern: "create", true)]
public async Task<ApiResult> CreateAsync([FromBody] XxxCreateCommand cmd)
{
    var entity = cmd.Map<XxxEntity>();
    await DB.Insertable(entity).ExecuteCommandAsync();
    return ApiResult.Success();
}
```

### 更新

``` csharp
[RoutePattern(pattern: "update", true)]
public async Task<ApiResult> UpdateAsync([FromBody] XxxUpdateCommand cmd)
{
    var entity = await DB.Queryable<XxxEntity>().FirstAsync(x => x.Id == cmd.Id)
        ?? throw new UserFriendlyException("记录不存在");
    cmd.Map(entity);
    await DB.Updateable(entity).ExecuteCommandAsync();
    return ApiResult.Success();
}
```

### 删除

``` csharp
[RoutePattern(pattern: "delete/{id}", true)]
public async Task<ApiResult> DeleteAsync(long id)
{
    await DB.Deleteable<XxxEntity>().Where(x => x.Id == id).ExecuteCommandAsync();
    return ApiResult.Success();
}
```

### 批量删除

``` csharp
[RoutePattern(pattern: "batchDelete", true, HttpMethod = "Delete")]
public async Task<ApiResult> BatchDeleteAsync([FromBody] List<long> ids)
{
    await DB.Deleteable<XxxEntity>().Where(x => ids.Contains(x.Id)).ExecuteCommandAsync();
    return ApiResult.Success();
}
```

## 验证

使用 FluentValidation 在 Command/DTO 中定义规则，框架自动校验。

``` csharp
public class XxxCreateValidator : AbstractValidator<XxxCreateCommand>
{
    public XxxCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
```

校验失败时会抛出 `MasaValidatorException`，全局异常处理中间件会捕获并返回标准错误响应。

## 事务

在方法上使用 `[UnitOfWork]` 特性启用事务，可指定隔离级别。

``` csharp
[UnitOfWork(isolationLevel: IsolationLevel.ReadCommitted)]
public async Task<ApiResult> CreateWithItemsAsync([FromBody] XxxCommand cmd)
{
    await DB.Insertable(cmd.Main).ExecuteCommandAsync();
    await DB.Insertable(cmd.Items).ExecuteCommandAsync();
    return ApiResult.Success();
}
```

## 当前用户

通过 `ICurrentUser` 获取当前登录用户信息。

``` csharp
private ICurrentUser CurrentUser => GetRequiredService<ICurrentUser>();
// CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles
```

可用于自动填充 Creator、Modifier 等字段。

## 常见问题

- **依赖注入失败**：检查是否调用了 `AddCoreAssembly` 和 `AddCoreAutoInject`。
- **路由无效**：确保 `RoutePattern` 的 `isAutoMapping` 为 `true`，且在 `app.MapMasaMinimalAPIs()` 之前注册。
- **验证不触发**：检查 Dto 类是否有对应的 Validator，确保 Validator 类被 DI 扫描到。