import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import { pathToFileURL } from 'node:url'
export const repository = 'usa-mishin/KaihenShelf'
export const packageId = 'com.usamishin.kaihenshelf.core'
export const listingUrl = 'https://usa-mishin.github.io/KaihenShelf/index.json'
export function buildListing(entries) {
  const versions = {}
  for (const { manifest, sha256 } of entries) {
    if (manifest.name !== packageId || !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(manifest.version)) throw Error('Invalid package identity/version')
    const expected = `https://github.com/${repository}/releases/download/v${manifest.version}/${packageId}-${manifest.version}.zip`
    if (manifest.url !== expected || !/^[a-f0-9]{64}$/.test(sha256)) throw Error('Invalid download URL or checksum')
    if (versions[manifest.version]) throw Error('Duplicate package version')
    versions[manifest.version] = { ...manifest, zipSHA256: sha256 }
  }
  if (!Object.keys(versions).length) throw Error('No published packages; refusing to publish an empty listing')
  return { name: 'KaihenShelf — Usatrot Works', id: 'com.usamishin.kaihenshelf.repo', url: listingUrl, author: 'Usatrot Works', packages: { [packageId]: { versions } } }
}
async function get(url, api = false) {
  const headers = { 'User-Agent': 'KaihenShelf-release' }
  if (api && process.env.GH_TOKEN) headers.Authorization = `Bearer ${process.env.GH_TOKEN}`
  const response = await fetch(url, { headers })
  if (!response.ok) throw Error(`HTTP ${response.status}: ${url}`)
  return response
}
async function main() {
  const entries = []
  if (process.argv.includes('--local')) {
    const manifest = JSON.parse(await readFile('Packages/' + packageId + '/package.json', 'utf8'))
    const zip = await readFile(`artifacts/${packageId}-${manifest.version}.zip`)
    entries.push({ manifest, sha256: createHash('sha256').update(zip).digest('hex') })
  } else {
    // Rebuild from every immutable published release, not only the latest version.
    for (let page = 1; ; page++) {
      const releases = await (await get(`https://api.github.com/repos/${repository}/releases?per_page=100&page=${page}`, true)).json()
      for (const release of releases.filter(r => !r.draft)) {
        const asset = release.assets.find(a => a.name === 'package.json')
        if (!asset) throw Error(`Missing manifest in published release ${release.tag_name}`)
        const manifest = await (await get(asset.browser_download_url)).json()
        if (release.tag_name !== `v${manifest.version}`) throw Error('Tag/manifest version mismatch')
        const zipAsset = release.assets.find(a => a.name === `${packageId}-${manifest.version}.zip`)
        if (!zipAsset || zipAsset.browser_download_url !== manifest.url) throw Error('Missing or mismatched ZIP')
        const zip = Buffer.from(await (await get(zipAsset.browser_download_url)).arrayBuffer())
        entries.push({ manifest, sha256: createHash('sha256').update(zip).digest('hex') })
      }
      if (releases.length < 100) break
    }
  }
  const listing = buildListing(entries)
  await mkdir('site/public', { recursive: true })
  await writeFile('site/public/index.json', JSON.stringify(listing, null, 2) + '\n')
  console.log(`VPM listing: ${entries.length} version(s)`)
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main()
