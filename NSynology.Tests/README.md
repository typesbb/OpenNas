# NSynology 集成测试

针对真实群晖 NAS 的官方 App 相册上传测试（需内网可达）。

## 配置

1. 复制 `nas.integration.sample.json` 为 `nas.integration.local.json`（已在 `.gitignore`，勿提交密码）。
2. 填写 `BaseUrl`、`Username`、`Password`、`LocalImageDirectory`。
3. 可选环境变量：`NAS_BASE_URL`、`NAS_USER`、`NAS_PASSWORD`、`NAS_IMAGE_DIR`。

## 运行

```bash
dotnet test NSynology.Tests/NSynology.Tests.csproj --filter "FullyQualifiedName~OfficialAppAlbumUploadTests"
```

## 用例

| 测试 | 说明 |
|------|------|
| `Official_app_album_upload_request_matches_mobile_capture_contract` | **离线**：Mock HTTP，断言 v5/upload、`album_id`、`require_thumb_version`、multipart 字段符合官方 App 抓包 |
| `Official_app_album_upload_v5_succeeds_on_real_nas_without_photos_subsession` | **集成**：Cookie `id`+`did` 主会话，上传到 `RemoteAlbumName` 相册 |

## 常见失败

| 错误 | 说明 |
|------|------|
| **108** | multipart 或 Cookie 会话不符合官方 App；检查 DSM Photos 权限 |
| **119** | 登录会话异常；加密登录勿带 `enable_syno_token=yes` |
