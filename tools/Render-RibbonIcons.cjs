// Optional icon regeneration: provide sharp through Node's package resolution.
const path = require('path');
const sharp = require('sharp');
const folder = path.resolve(__dirname, '../src/BIM.Tools/Ribbon/Images');
(async () => {
  for (const size of [16, 32]) {
    await sharp(path.join(folder, 'BimSectionBox.svg'), { density: 384 })
      .resize(size, size).png().toFile(path.join(folder, `BimSectionBox${size}.png`));
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
