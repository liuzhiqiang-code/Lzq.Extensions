# Lzq.Core

`Lzq.Core` 
## 🌟 核心特性

Masa动态MiniApi:https://docs.masastack.com/framework/building-blocks/minimal-apis#
异常类型,异常类型中间件:https://docs.masastack.com/framework/building-blocks/exception#
依赖注入：https://docs.masastack.com/framework/utils/extensions/dependency-injection#
常用扩展：https://docs.masastack.com/framework/utils/extensions/dotnet-extensions#
枚举扩展：https://docs.masastack.com/framework/utils/extensions/enums#
加密/解密帮助类：https://docs.masastack.com/framework/utils/security/cryptography#
Mapster: https://www.mapster.org/# 
ApiResult等常用结果类

## 🚀 快速开始
``` C#
builder.Services.AddCoreAssembly().AddCoreAutoInject();
builder.Services.AddCoreMinimalAPIs();

var app = builder.Build();

app.UseCoreExceptionHandler();
app.MapMasaMinimalAPIs();
```