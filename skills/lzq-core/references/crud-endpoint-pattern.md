# CRUD 端点速查模板

## 完整 Service 模板（复制后修改 `Xxx` 即可）

```csharp
public class XxxService : ServiceBase
{
    public XxxService() : base("/api/v1/xxx") { }
    private ISqlSugarClient DB => GetRequiredService<ISqlSugarClient>();
    private ILogger<XxxService> Logger => GetRequiredService<ILogger<XxxService>>();

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

    [RoutePattern(pattern: "detail", true)]
    public async Task<ApiResult> DetailAsync(long id)
    {
        var entity = await DB.Queryable<XxxEntity>().FirstAsync(x => x.Id == id);
        return entity == null
            ? ApiResult.Fail("记录不存在", 404)
            : ApiResult.Success(entity.Map<XxxViewDto>());
    }

    [RoutePattern(pattern: "create", true)]
    public async Task<ApiResult> CreateAsync([FromBody] XxxCreateCommand cmd)
    {
        var entity = cmd.Map<XxxEntity>();
        await DB.Insertable(entity).ExecuteCommandAsync();
        return ApiResult.Success();
    }

    [RoutePattern(pattern: "update", true)]
    public async Task<ApiResult> UpdateAsync([FromBody] XxxUpdateCommand cmd)
    {
        var entity = await DB.Queryable<XxxEntity>().FirstAsync(x => x.Id == cmd.Id)
            ?? throw new UserFriendlyException("记录不存在");
        cmd.Map(entity);
        await DB.Updateable(entity).ExecuteCommandAsync();
        return ApiResult.Success();
    }

    [RoutePattern(pattern: "delete/{id}", true)]
    public async Task<ApiResult> DeleteAsync(long id)
    {
        await DB.Deleteable<XxxEntity>().Where(x => x.Id == id).ExecuteCommandAsync();
        return ApiResult.Success();
    }

    [RoutePattern(pattern: "batchDelete", true, HttpMethod = "Delete")]
    public async Task<ApiResult> BatchDeleteAsync([FromBody] List<long> ids)
    {
        await DB.Deleteable<XxxEntity>().Where(x => ids.Contains(x.Id)).ExecuteCommandAsync();
        return ApiResult.Success();
    }
}

```

## 完整 DTO 模板
``` csharp
// 创建命令
public record XxxCreateCommand(string Name, /* 其他必填字段 */);

// 更新命令
public record XxxUpdateCommand(long Id, string Name, /* 其他字段 */);

// 列表视图
public record XxxViewDto(long Id, string Name, EnableStatusEnum Status, DateTime CreationTime);

// 分页请求
public record XxxPageRequest : PagedRequest
{
    public string? Keyword { get; set; }
}
```

## 端点速查

| 操作     | 方法 | 路由示例 | 说明               |
| ---------- | ------ | ---------- | -------------------- |
| 分页查询 | `POST`     | `/api/v1/xxx/page`         | 支持关键字搜索     |
| 详情查询 | `POST`     | `/api/v1/xxx/detail?id=1`         | 按 ID 获取单条记录 |
| 创建     | `POST`     | `/api/v1/xxx/create`         | 插入新记录         |
| 更新     | `POST`     | `/api/v1/xxx/update`         | 按 ID 更新         |
| 删除     | `POST`     | `/api/v1/xxx/delete/1`         | 按 ID 删除         |
| 批量删除 | `DELETE`     | `/api/v1/xxx/batchDelete`         | 传入 ID 数组       |

## 常见扩展

### 关联查询

``` csharp
var query = DB.Queryable<XxxEntity>()
    .LeftJoin<YyyEntity>((x, y) => x.YyyId == y.Id)
    .Select((x, y) => new XxxViewDto
    {
        Id = x.Id,
        Name = x.Name,
        YyyName = y.Name
    });
```

### 导入导出

``` csharp
// 导出 Excel
var bytes = await DB.Queryable<XxxEntity>().ToExcelStreamAsync();

// 批量导入
var list = ExcelHelper.ReadFromExcel<XxxCreateCommand>(stream);
await DB.Insertable(list.Map<List<XxxEntity>>()).ExecuteCommandAsync();
```