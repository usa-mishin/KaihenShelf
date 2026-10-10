import { test } from 'node:test'
import assert from 'node:assert/strict'
import { buildListing, packageId } from '../../scripts/build-listing.mjs'
const entry = version => ({ manifest: { name: packageId, version, url: `https://github.com/usa-mishin/KaihenShelf/releases/download/v${version}/${packageId}-${version}.zip` }, sha256: 'a'.repeat(64) })
test('keeps older versions and download checksums', () => {
  const result = buildListing([entry('0.1.140'), entry('0.1.141')])
  assert.deepEqual(Object.keys(result.packages[packageId].versions), ['0.1.140', '0.1.141'])
  assert.equal(result.packages[packageId].versions['0.1.140'].zipSHA256, 'a'.repeat(64))
})
test('fails closed on empty listing and duplicate versions', () => {
  assert.throws(() => buildListing([]))
  assert.throws(() => buildListing([entry('0.1.140'), entry('0.1.140')]))
})
test('rejects unexpected identity, URL, version and checksum', () => {
  for (const property of ['name', 'url', 'version']) {
    const bad = entry('0.1.140'); bad.manifest[property] = 'invalid'; assert.throws(() => buildListing([bad]))
  }
  assert.throws(() => buildListing([{ ...entry('0.1.140'), sha256: 'bad' }]))
})
