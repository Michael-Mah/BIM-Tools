// Rasterize the generated masters without carrying high-DPI metadata into WPF.
// Navisworks renders 32 px at 384 DPI as only 8 device-independent pixels.
const path = require('path');
const sharp = require('sharp');
const folder = path.resolve(__dirname, '../src/BIM.Tools/Ribbon/Images');
const masters = path.resolve(__dirname, '../assets/icons/masters');
(async () => {
  for (const name of ['BimFederationAudit', 'BimSectionBox', 'BimModelSharing', 'BimClash']) {
    for (const size of [16, 32, 64]) {
      await sharp(path.join(masters, `${name}.png`))
        .resize(size, size, { kernel: 'lanczos3' })
        .withMetadata({ density: 96 })
        .png()
        .toFile(path.join(folder, `${name}${size}.png`));
      const metadata = await sharp(path.join(folder, `${name}${size}.png`)).metadata();
      if (metadata.width !== size || metadata.height !== size || metadata.density !== 96 || !metadata.hasAlpha)
        throw new Error(`Invalid ribbon dimensions, DPI or transparency: ${name}${size}.png`);
    }
  }
  console.log('Generated four icon families at 16/32/64 px, 96 DPI, with transparency.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
