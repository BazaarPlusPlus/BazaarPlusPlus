# BazaarPlusPlus Product Release

## Language

**Product Release**:
同一产品版本的 mod、installer 与对外发布记录。产品版本不表示数据库 schema、native ABI 或用户当前已安装版本。

**Payload**:
installer 携带并安装到游戏目录的文件集合。文件的归属决定安装、修复和卸载可以修改哪些路径。

**Payload Inventory**:
Payload 中当前文件、平台适用范围、文件归属及退休文件的共同事实。用户数据与第三方插件不属于发布清单。

**Release Manifest**:
已发布 Product Release 的版本、平台更新包和实际安装器下载地址。它不代表某一个平台刚完成上传的状态。

**Release Promotion**:
在声明的平台产物全部就绪后，将 Product Release 设为 latest 的操作。重复执行同一发布不改变已有产物。
