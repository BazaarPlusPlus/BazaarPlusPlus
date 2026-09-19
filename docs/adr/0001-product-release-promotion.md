# 产品版本与完整发布提升

mod 是 installer 的 Payload，不是独立发布的产品。采用根目录单一产品版本和共同 Payload Inventory，但保留 schema、ABI、用户数据格式及已安装版本的独立语义；清单共享不能扩大卸载权限，也不能合并 runtime catalog、Release Manifest 和 Build Seed Fetch 的生命周期。

两个平台允许独立构建、上传，但必须来自相同产品版本和 Git 提交，完整校验后才提升 latest。版本路径不可变，latest 使用 R2 S3 的 ETag 条件写。拒绝“某个平台上传后顺手更新 latest”和无条件覆写：前者会暴露半个版本，后者可能让较慢的旧发布覆盖新发布。代价是需要双平台协调与 S3 凭据，并且修复已发布产物必须增加产品版本。

本地准备与 installer 打包共用锁；来源记录绑定输入和 unsigned 产出，正式签名后的分发 hash 另行记录。这保留 mod 负责 native 生产、installer 负责正式签名的既有决定。
