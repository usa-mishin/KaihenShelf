import { readFile, writeFile } from 'node:fs/promises'
// Keep the published Japanese policy synchronized with the source policy.
await writeFile('site/policy.md', await readFile('docs/PRIVACY.md', 'utf8'))
