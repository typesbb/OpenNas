# OpenNas

<p align="center">
  <img src="https://img.shields.io/badge/platform-Android%20%7C%20iOS%20%7C%20Windows-0D9488" alt="Platform" />
  <img src="https://img.shields.io/badge/Synology-Photos-yellow" alt="Synology Photos" />
</p>

**OpenNas** 是一款面向群晖 NAS 用户的相册应用，连接 **Synology Photos**，让你在手机上随时浏览 NAS 里的照片与视频，并把手机相册自动备份回家。

---

## 特色亮点

- **专为 Synology Photos 设计** — 直接浏览 NAS 相册，查看高清原图，播放视频（Android 支持画中画小窗）
- **内外网无缝切换** — 分别配置内网地址与外网地址（QuickConnect / DDNS），在家、外出都能连；可开启自动切换
- **相册自动备份** — 按本机相册（相机、截图等）建立规则，后台上传到 NAS 指定相册，支持进度查看与失败重试（目前 Android）
- **灵活的照片管理** — 新建相册、上传照片、多选移动或删除，按时间 / 名称 / 大小排序浏览
- **省心又安全** — 支持「仅 Wi-Fi 备份」；备份后删除本地文件需二次确认，且仅在 NAS 确认上传成功后才执行
- **适配家庭 NAS 环境** — 支持内网自签 HTTPS 证书，无需额外折腾证书配置
- **深色模式** — 跟随系统，也可手动切换浅色 / 深色

> 目前 **Android 版功能最完整**；iOS 与 Windows 已支持相册浏览，自动备份正在适配中。

---

## 下载

| 平台 | 说明 |
|------|------|
| **Android** | 在 [Releases](https://github.com/typesbb/OpenNas/releases/latest) 下载最新 APK 安装 |
| **iOS / Windows** | 需自行编译，或等待后续发布 |

---

## 使用前准备

- **NAS**：群晖 DSM 已安装并启用 **Synology Photos（相册）**
- **账号**：NAS 的 DSM 用户名与密码
- **网络**：知道 NAS 的内网地址（如 `192.168.x.x:5001`）；若需外网访问，准备好 QuickConnect 或 DDNS 地址

---

## 快速开始

### 1. 配置 NAS 连接

在登录页点击 **「连接设置」**（或「我的」→ **连接设置**）：

| 项目 | 说明 |
|------|------|
| **内网 (LAN)** | 在家或与 NAS 同一 Wi-Fi 时使用，填 IP 或域名 |
| **外网 (WAN)** | 外出时使用，填 QuickConnect 或 DDNS 地址 |
| **自动切换** | 开启后，应用会根据当前网络自动选择内网或外网地址 |

地址格式通常为 `https://你的地址:5001`（端口常见为 5001）。

![连接设置](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/connection-settings.jpg)

### 2. 登录

输入 DSM **用户名**和**密码**，点击 **登录**。

- 登录状态会保持，下次打开可自动进入
- 登录页显示当前 NAS 地址，便于确认连的是哪一台

![登录页](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/login.jpg)

---

## 功能介绍

应用底部四个标签：**相册**、**文件**、**任务**、**我的**。

### 相册 — 浏览与管理 NAS 照片

- 网格浏览全部相册，显示照片数量；支持排序、新建相册、下拉刷新
- 进入相册后可按 **拍摄时间**、**文件名称**、**文件大小** 排序，照片分组展示
- 点击照片全屏查看原图：双指缩放、左右滑动切换、下滑关闭
- 视频可直接播放；Android 上支持画中画小窗
- **长按**进入多选，可批量 **移动** 到其他相册或 **删除**
- 右上角 **+** 可从手机选取照片 **上传到当前相册**

![相册列表](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/albums.jpg)&emsp;![相册排序](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/albums-sort.jpg)

![相册详情](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/album-detail.jpg)&emsp;![照片排序](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/album-detail-sort.jpg)

### 任务 — 手机相册自动备份

将本机指定相册（如「相机」「截图」）的照片/视频，按规则自动备份到 NAS 上的目标相册（**目前仅 Android**）。

**添加规则**（右上角 +）：

1. 选择本机相册
2. 选择 NAS 目标相册（或新建一个）
3. 可选：备份成功后删除手机上的原文件（请谨慎，删除不可恢复）

**管理规则**：启用/停用、切换「备份后删除」、查看进度与队列、失败重试、运行中暂停。

可在「我的」→ **备份策略** 中设置 **仅 Wi-Fi 时备份**。

![任务 - 空状态](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/tasks-empty.jpg)&emsp;![备份后删除确认](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/tasks-delete-confirm.jpg)

![备份规则](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/tasks-rule.jpg)&emsp;![备份进度](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/tasks-progress.jpg)

### 文件

File Station 文件浏览与管理 **正在开发中**。目前可通过「相册」浏览照片，通过「任务」配置备份。

### 我的 — 账号与设置

| 功能 | 说明 |
|------|------|
| **切换** | 快速切换已保存的 NAS 连接 |
| **连接设置** | 编辑内网/外网地址，开启自动切换 |
| **备份策略** | 仅 Wi-Fi 备份；备份后删除的风险确认 |
| **外观** | 跟随系统 / 浅色 / 深色 |
| **清理缓存** | 释放浏览产生的缩略图与原图缓存 |
| **日志** | 查看备份与操作记录，排查问题 |
| **退出登录** | 返回登录页 |

![我的](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/profile.jpg)&emsp;![备份策略](https://gcore.jsdelivr.net/gh/typesbb/OpenNas@master/docs/screenshots/backup-settings.jpg)

---

## 备份与删除说明

- **备份后删除** 仅在文件 **确认已成功上传到 NAS** 后，才删除手机上的副本
- 删除 **不可恢复**，请务必确认 NAS 上已有完整备份
- 首次开启「备份后删除」前，需在 **「我的」→ 备份策略** 阅读并确认风险说明
- 建议开启 **「仅 Wi-Fi 时备份」**，避免消耗移动数据

---

## 应用权限

| 权限 | 用途 |
|------|------|
| 网络 | 连接 NAS，上传与下载照片/视频 |
| 读取照片与视频 | 扫描本机相册以执行备份 |
| 通知 | 备份任务进行时的状态提醒 |

首次添加备份规则时，若未授权访问照片，应用会引导你开启。

---

## 常见问题

**打不开 NAS 或登录失败？**

- 检查地址是否含 `https://` 与正确端口号（常见 `5001`）
- 内网连接时确认手机与 NAS 在同一局域网
- 外网连接时确认 QuickConnect / DDNS 可从当前网络访问
- 核对 DSM 用户名、密码

**相册是空的？**

- 确认 NAS 上 Synology Photos 中已有相册与照片
- 下拉刷新相册列表

**备份一直不动或失败？**

- 检查 NAS 是否在线、网络是否正常
- 在「任务」中点 **重试**；在「我的」→ **日志** 查看详情
- 确认已授权访问照片；若开启了「仅 Wi-Fi」，需连接 Wi-Fi

**手机存储占用变大？**

- 浏览照片会产生缓存，可在「我的」→ **清理缓存** 释放空间

---

OpenNas — 让 NAS 相册触手可及。
