# 核心代码说明

## Unity

`InspectionApp.BuildAR` 组合 ARSession、XROrigin、ARCameraManager/Background、TrackedPoseDriver、ARPlaneManager、ARRaycastManager。平面只请求 `Horizontal`。XR provider 配置由 AndroidSetup 菜单及 XR Plug-in Management 保存。

`Update` 仅在 `SessionTracking` 且没有提交请求时处理单指触摸开始。`TryPlace` 先针对该触摸位置执行 EventSystem 的 UI raycast，命中任意 UI 即退出；再用 ARRaycastManager 的 `PlaneWithinPolygon` 查询。查不到真实平面不放置。标记根节点保存真实命中点；可见立方体向上偏移半个边长，避免一半埋在平面下。

`PlaneOutline` 根据真实 ARPlane 的 boundary 绘制绿色边界。它不创建假的平面，只呈现 AR 子系统提供的数据。

`Submit` 做标题、标记及地址检查，将同一标记的世界坐标与表单一起序列化。`Send` 使用 UTF-8 JSON，通过 UnityWebRequest POST。失败保留表单和标记；成功取得后端 ID 才清理表单并允许下一次放置。服务器地址保存在 PlayerPrefs，仅用于下一次启动恢复地址。

## Go

`Issue` 与 Unity/React 保持相同 JSON 字段，客户端不能设置服务端 ID、状态、时间。请求坐标使用指针以区分“没有传值”和“传了 0”。标题按 Unicode 字符数校验，支持中文。

`Store.save` 不直接修改当前切片。调用者拿写锁后构建候选快照，写完并替换文件成功才赋给 `s.issues`。所以磁盘写入失败不会造成“列表显示成功而重启丢失”的内存状态。读取在读锁下复制切片，排序不会修改共享数据。一个文件仅供一个进程写入。

`openStore` 在启动时加载并校验数据。遇到损坏内容停止启动。状态更新找不到 ID 返回 404，参数错误返回 400，存储错误返回 500。健康检查表示进程可响应，不保证磁盘永远可写；每次修改仍需实际检查保存结果。

## React

`api.ts` 集中处理 10 秒超时、HTTP 错误与网络错误。页面有加载、空列表和错误提示状态。更新状态使用受控下拉框：收到服务器成功响应才更新本地记录，失败时保留旧值。状态修改期间禁用其他修改与刷新，避免同一页面内请求乱序覆盖。

搜索和筛选均在已读取的数据上执行；“刷新列表”重新读取 Go。详情页显示描述、会话坐标与服务端更新时间。React 不直接读写 JSON 文件。

## 如何检查 AI 代码

1. 不用“能编译”替代行为验证：读取/写入/失败/重启都要检查。
2. 不用接口造数替代手机证据：API 测试只证明服务和网页工作。
3. 不给缺失字段自动补出看似合理的业务值：坐标缺一个分量也必须报错。
4. 可解释 UI 防误触与文件写入顺序的原因，并在真机/故障注入中验证。

本说明帮助理解实现，不代表候选人已亲自复核或能独立解释；提交前应阅读并实际走通代码。
