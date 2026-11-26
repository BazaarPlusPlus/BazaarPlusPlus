//! `card-catalog <Data dir> <GameData.db.zip> <out dir> --zh-etag <etag> [--buildid <id>]
//! [--gamedata-etag <etag>] [--previous <index.json>] [--require-fx]`
//!
//! Publication's extraction; `zh-CN.bytes` sits beside the GameData zip.

use anyhow::{bail, Context, Result};
use std::io::Read;
use std::path::PathBuf;

fn main() -> Result<()> {
    let mut positional = Vec::new();
    let (mut buildid, mut gamedata_etag, mut zh_etag, mut previous, mut require_fx) =
        (None, None, None, None, false);
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        let mut value = || args.next().with_context(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--buildid" => buildid = Some(value()?),
            "--gamedata-etag" => gamedata_etag = Some(value()?),
            "--zh-etag" => zh_etag = Some(value()?),
            "--previous" => previous = Some(PathBuf::from(value()?)),
            "--require-fx" => require_fx = true,
            flag if flag.starts_with("--") => bail!("unknown option {flag}"),
            _ => positional.push(PathBuf::from(arg)),
        }
    }
    let [data_dir, game_data_zip, out_dir] = positional.as_slice() else {
        bail!("usage: card-catalog <Data dir> <GameData.db.zip> <out dir> --zh-etag <etag> [--buildid <id>] [--gamedata-etag <etag>] [--previous <index.json>] [--require-fx]");
    };
    let zh_etag = zh_etag.context("--zh-etag is required")?;
    let scratch = tempfile::tempdir()?;
    let game_data = scratch.path().join("GameData.db");
    {
        let mut archive = zip::ZipArchive::new(std::fs::File::open(game_data_zip)?)?;
        let name = archive
            .file_names()
            .filter_map(Result::ok)
            .find(|n| n.ends_with("GameData.db"))
            .map(std::borrow::Cow::into_owned)
            .context("no GameData.db in the archive")?;
        let mut bytes = Vec::new();
        archive.by_name(&name)?.read_to_end(&mut bytes)?;
        std::fs::write(&game_data, bytes)?;
    }
    let translations = game_data_zip.canonicalize()?.with_file_name("zh-CN.bytes");
    let summary = card_catalog::extract(&card_catalog::Inputs {
        data_dir,
        game_data: &game_data,
        translations: &translations,
        previous: previous.as_deref(),
        out_dir,
        buildid,
        gamedata_etag,
        zh_etag: Some(zh_etag),
        publication: true,
        require_fx,
    })?;
    println!("{}", summary.json());
    Ok(())
}
