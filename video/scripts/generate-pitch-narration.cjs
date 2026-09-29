const { MsEdgeTTS, OUTPUT_FORMAT } = require('msedge-tts');
const fs = require('fs');
const path = require('path');

// Narration text is shared with the scenes (captions) in src/aiDevopsOctopusPitch.narration.json.
const segments = require('../src/aiDevopsOctopusPitch.narration.json');

const VOICE = 'en-US-AndrewNeural';
const RATE = process.env.PITCH_RATE || '+8%';
const outDir = path.join(__dirname, '..', 'public', 'audio');

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  for (const seg of segments) {
    const tts = new MsEdgeTTS();
    await tts.setMetadata(VOICE, OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3);
    const { audioFilePath } = await tts.toFile(outDir, seg.spoken, { rate: seg.rate || RATE });
    const target = path.join(outDir, `pitch-${seg.id}.mp3`);
    fs.renameSync(audioFilePath, target);
    console.log('wrote', target);
  }
})().catch((e) => {
  console.error('FAIL', e);
  process.exit(1);
});
