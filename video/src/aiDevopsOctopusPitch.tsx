import React from 'react';
import {
  AbsoluteFill,
  Series,
  Sequence,
  Audio,
  staticFile,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';
import {theme, FPS} from './theme';
import {Scene, Eyebrow, Rise} from './components';
import narration from './aiDevopsOctopusPitch.narration.json';

/** The single accent color; green is reserved for pass/deployed status and always ships with a ✓. */
const ACCENT = theme.series1;
const FADE = 10;
const AUDIO_LEAD = 6;

type Segment = {id: string; seconds: number; caption: string; spoken: string};
const SEGMENTS = narration as Segment[];
const captionFor = (id: string) => SEGMENTS.find((s) => s.id === id)!.caption;

const BOARD_COLUMNS = [
  'Todo',
  'In Progress',
  'In Review',
  'Deployed to TDD',
  'Deployed to UAT',
  'Deployed to Prod',
  'Done',
];

const useSpring = (delay = 0, damping = 200) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  return spring({frame: frame - delay, fps, config: {damping}});
};

const Fade: React.FC<{durationInFrames: number; children: React.ReactNode}> = ({
  durationInFrames,
  children,
}) => {
  const frame = useCurrentFrame();
  const opacity = interpolate(
    frame,
    [0, FADE, durationInFrames - FADE, durationInFrames],
    [0, 1, 1, 0],
    {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}
  );
  return <AbsoluteFill style={{opacity}}>{children}</AbsoluteFill>;
};

/** On-screen caption; text is the same narration line the voice reads. */
const Caption: React.FC<{id: string}> = ({id}) => (
  <div
    style={{
      position: 'absolute',
      left: 110,
      right: 110,
      bottom: 56,
      fontSize: 30,
      lineHeight: 1.4,
      color: theme.ink,
      background: 'rgba(13,13,13,0.82)',
      border: `1px solid ${theme.border}`,
      borderRadius: 12,
      padding: '16px 26px',
      fontFamily: theme.font,
      textAlign: 'center',
    }}
  >
    {captionFor(id)}
  </div>
);

const Headline: React.FC<{children: React.ReactNode; size?: number; color?: string}> = ({
  children,
  size = 64,
  color = theme.ink,
}) => (
  <div style={{fontSize: size, fontWeight: 700, lineHeight: 1.1, letterSpacing: -1.2, color}}>
    {children}
  </div>
);

const Check: React.FC<{show: number; size?: number}> = ({show, size = 30}) => (
  <span
    style={{
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: size + 12,
      height: size + 12,
      borderRadius: '50%',
      background: show > 0.5 ? theme.good : theme.gridline,
      color: theme.ink,
      fontSize: size * 0.8,
      fontWeight: 700,
      transform: `scale(${0.7 + 0.3 * show})`,
      flexShrink: 0,
    }}
  >
    {show > 0.5 ? '✓' : ''}
  </span>
);

/** A row that ticks from pending to passed at `at` frames. */
const CheckRow: React.FC<{label: string; detail?: string; at: number; size?: number}> = ({
  label,
  detail,
  at,
  size = 34,
}) => {
  const appear = useSpring(at - 14);
  const done = useSpring(at);
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 20,
        opacity: appear,
        transform: `translateX(${interpolate(appear, [0, 1], [-24, 0])}px)`,
        marginTop: 20,
      }}
    >
      <Check show={done} size={size * 0.9} />
      <div>
        <div style={{fontSize: size, fontWeight: 600}}>{label}</div>
        {detail ? <div style={{fontSize: 24, color: theme.inkMuted, marginTop: 2}}>{detail}</div> : null}
      </div>
    </div>
  );
};

const Card: React.FC<{children: React.ReactNode; style?: React.CSSProperties; accent?: string}> = ({
  children,
  style,
  accent,
}) => (
  <div
    style={{
      background: theme.surface,
      border: `1px solid ${accent ?? theme.border}`,
      borderRadius: 18,
      padding: '26px 30px',
      ...style,
    }}
  >
    {children}
  </div>
);

/**
 * The project board: seven columns and one card whose position is a fractional
 * column index, so the card glides between columns.
 */
const Board: React.FC<{position: number; label: string; compact?: boolean; highlightFrom?: number}> = ({
  position,
  label,
  compact = false,
  highlightFrom = 0,
}) => {
  const colWidth = 232;
  const gap = 10;
  const height = compact ? 190 : 330;
  return (
    <div style={{position: 'relative', display: 'flex', gap, height}}>
      {BOARD_COLUMNS.map((col, i) => {
        const active = Math.round(position) === i;
        return (
          <div
            key={col}
            style={{
              width: colWidth,
              background: active ? 'rgba(57,135,229,0.10)' : theme.plane,
              border: `1px solid ${active ? ACCENT : theme.border}`,
              borderRadius: 14,
              padding: '14px 14px',
              opacity: i < highlightFrom ? 0.55 : 1,
            }}
          >
            <div style={{fontSize: 22, fontWeight: 600, color: active ? ACCENT : theme.inkSecondary}}>{col}</div>
          </div>
        );
      })}
      <div
        style={{
          position: 'absolute',
          top: 62,
          left: 12 + position * (colWidth + gap),
          width: colWidth - 24,
          background: theme.surface,
          border: `2px solid ${ACCENT}`,
          borderRadius: 12,
          padding: '12px 14px',
          boxShadow: '0 12px 30px rgba(0,0,0,0.45)',
        }}
      >
        <div style={{fontSize: 20, fontWeight: 700, color: ACCENT}}>#40</div>
        <div style={{fontSize: 18, color: theme.ink, lineHeight: 1.3, marginTop: 4}}>{label}</div>
      </div>
    </div>
  );
};

/** Card position animated through a list of [frame, column] keyframes. */
const useBoardPosition = (keys: [number, number][]) => {
  const frame = useCurrentFrame();
  return interpolate(
    frame,
    keys.map((k) => k[0]),
    keys.map((k) => k[1]),
    {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}
  );
};

/** A horizontal arrow drawn with SVG that grows from left to right. */
const Arrow: React.FC<{progress: number; width?: number}> = ({progress, width = 110}) => (
  <svg width={width} height={40} style={{flexShrink: 0}}>
    <line x1={4} y1={20} x2={4 + (width - 22) * progress} y2={20} stroke={ACCENT} strokeWidth={5} strokeLinecap="round" />
    {progress > 0.95 ? (
      <polygon points={`${width - 20},8 ${width - 2},20 ${width - 20},32`} fill={ACCENT} />
    ) : null}
  </svg>
);

const Node: React.FC<{title: string; sub?: string; show: number; accent?: boolean; width?: number}> = ({
  title,
  sub,
  show,
  accent = false,
  width = 360,
}) => (
  <div
    style={{
      width,
      opacity: show,
      transform: `scale(${0.9 + 0.1 * show})`,
      background: accent ? 'rgba(57,135,229,0.14)' : theme.surface,
      border: `2px solid ${accent ? ACCENT : theme.baseline}`,
      borderRadius: 18,
      padding: '24px 26px',
    }}
  >
    <div style={{fontSize: 34, fontWeight: 700}}>{title}</div>
    {sub ? <div style={{fontSize: 24, color: theme.inkSecondary, marginTop: 8, lineHeight: 1.35}}>{sub}</div> : null}
  </div>
);

// ---------------------------------------------------------------- scenes

/** 1. Hook (0-8 s) */
const HookScene: React.FC = () => {
  const pos = useBoardPosition([
    [70, 0],
    [200, 6],
  ]);
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>AI DevOps with Octopus Deploy</Eyebrow>
      </Rise>
      <div style={{display: 'flex', gap: 28, marginTop: 20, flexWrap: 'wrap'}}>
        <Rise delay={8}>
          <Headline size={76}>A work item goes in.</Headline>
        </Rise>
        <Rise delay={40}>
          <Headline size={76}>Production software comes out.</Headline>
        </Rise>
        <Rise delay={80}>
          <Headline size={76} color={ACCENT}>
            No hand-offs.
          </Headline>
        </Rise>
      </div>
      <Rise delay={24} style={{marginTop: 56}}>
        <Board position={pos} label="Instructions character counter" />
      </Rise>
      <Caption id="hook" />
    </Scene>
  );
};

/** 2. The work item (8-18 s) */
const WorkItemScene: React.FC = () => {
  const pos = useBoardPosition([
    [190, 0],
    [230, 1],
  ]);
  const comment = useSpring(90);
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>The work item</Eyebrow>
      </Rise>
      <div style={{display: 'flex', gap: 40, marginTop: 22, alignItems: 'flex-start'}}>
        <Rise delay={6} style={{flex: 1}}>
          <Card>
            <div style={{fontSize: 30, color: theme.inkMuted}}>Issue #40 · open</div>
            <Headline size={52}>Show remaining-character counter on the work order Instructions field</Headline>
          </Card>
        </Rise>
        <div
          style={{
            flex: 1,
            opacity: comment,
            transform: `translateY(${interpolate(comment, [0, 1], [30, 0])}px)`,
          }}
        >
          <Card accent={ACCENT}>
            <div style={{fontSize: 26, color: ACCENT, fontWeight: 700}}>AI agent · design comment</div>
            <div style={{fontSize: 28, color: theme.inkSecondary, marginTop: 12, lineHeight: 1.45}}>
              Counter below the field: “3,873 characters remaining”. Updates on input. Tests: bUnit
              component, Playwright acceptance.
            </div>
          </Card>
        </div>
      </div>
      <Rise delay={20} style={{marginTop: 40}}>
        <Board position={pos} label="Instructions character counter" compact />
      </Rise>
      <Caption id="work-item" />
    </Scene>
  );
};

/** 3. Build and test (18-30 s) */
const BuildScene: React.FC = () => {
  const pr = useSpring(250);
  const files = [
    'WorkOrderManage.razor · counter markup',
    'InstructionsCounterTests.cs · bUnit',
    'InstructionsCounter.spec · Playwright',
  ];
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>Build and test</Eyebrow>
      </Rise>
      <Rise delay={4}>
        <Headline size={64}>Code and tests, written together</Headline>
      </Rise>
      <div style={{display: 'flex', gap: 60, marginTop: 34}}>
        <div style={{flex: 1}}>
          {files.map((f, i) => (
            <Rise key={f} delay={20 + i * 16}>
              <div
                style={{
                  fontFamily: 'Consolas, "Cascadia Code", monospace',
                  fontSize: 28,
                  background: theme.plane,
                  border: `1px solid ${theme.border}`,
                  borderLeft: `4px solid ${ACCENT}`,
                  borderRadius: 10,
                  padding: '16px 20px',
                  marginTop: 14,
                  color: theme.inkSecondary,
                }}
              >
                + {f}
              </div>
            </Rise>
          ))}
        </div>
        <div style={{flex: 1}}>
          <CheckRow label="Private build" detail="unit + integration tests" at={120} />
          <CheckRow label="Acceptance suite" detail="Playwright, full application" at={185} />
          <div
            style={{
              marginTop: 34,
              opacity: pr,
              transform: `scale(${0.92 + 0.08 * pr})`,
              display: 'inline-block',
              background: 'rgba(57,135,229,0.14)',
              border: `2px solid ${ACCENT}`,
              borderRadius: 14,
              padding: '16px 24px',
            }}
          >
            <div style={{fontSize: 26, color: theme.inkMuted}}>Pull request opened</div>
            <div style={{fontSize: 48, fontWeight: 700, color: ACCENT}}>Refs #40</div>
          </div>
        </div>
      </div>
      <Caption id="build" />
    </Scene>
  );
};

/** 4. CI gate (30-40 s) */
const CiGateScene: React.FC = () => {
  const frame = useCurrentFrame();
  const a1 = useSpring(30);
  const ci = useSpring(20);
  const green = frame > 110;
  const a2 = useSpring(170);
  const merge = useSpring(200);
  const spin = (frame * 8) % 360;
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>CI gate</Eyebrow>
      </Rise>
      <Rise delay={4}>
        <Headline size={64}>Nothing merges without a green check</Headline>
      </Rise>
      <div style={{display: 'flex', alignItems: 'center', gap: 14, marginTop: 44}}>
        <Node title="PR head" sub="Refs #40" show={1} width={260} />
        <Arrow progress={a1} />
        <div
          style={{
            width: 420,
            opacity: ci,
            background: theme.surface,
            border: `2px solid ${green ? theme.good : theme.baseline}`,
            borderRadius: 18,
            padding: '24px 26px',
            display: 'flex',
            alignItems: 'center',
            gap: 18,
          }}
        >
          {green ? (
            <Check show={1} size={36} />
          ) : (
            <svg width={48} height={48} style={{transform: `rotate(${spin}deg)`}}>
              <circle cx={24} cy={24} r={18} stroke={theme.baseline} strokeWidth={5} fill="none" />
              <path d="M24 6 A18 18 0 0 1 42 24" stroke={ACCENT} strokeWidth={5} fill="none" strokeLinecap="round" />
            </svg>
          )}
          <div>
            <div style={{fontSize: 34, fontWeight: 700, fontFamily: 'Consolas, monospace'}}>codefresh/ci</div>
            <div style={{fontSize: 24, color: green ? theme.good : theme.inkMuted}}>{green ? 'success' : 'running…'}</div>
          </div>
        </div>
        <Arrow progress={a2} />
        <Node title="Merge" sub="into master" show={merge} accent width={260} />
      </div>
      <Card style={{marginTop: 44, display: 'flex', gap: 50, alignItems: 'center'}}>
        <div style={{fontSize: 30, fontWeight: 700, color: ACCENT}}>Branch protection</div>
        {['Pull request required', 'Required status check', 'No bypass'].map((r, i) => (
          <Rise key={r} delay={50 + i * 14}>
            <div style={{fontSize: 30, color: theme.inkSecondary}}>🔒 {r}</div>
          </Rise>
        ))}
      </Card>
      <Caption id="ci-gate" />
    </Scene>
  );
};

/** 5. Release (40-52 s) */
const ReleaseScene: React.FC = () => {
  const n1 = useSpring(0);
  const a1 = useSpring(40);
  const n2 = useSpring(60);
  const a2 = useSpring(110);
  const n3 = useSpring(130);
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>Release</Eyebrow>
      </Rise>
      <Rise delay={4}>
        <Headline size={64}>Every merge becomes a release</Headline>
      </Rise>
      <div style={{display: 'flex', alignItems: 'center', gap: 14, marginTop: 50}}>
        <Node title="Merge" sub="master" show={n1} width={250} />
        <Arrow progress={a1} />
        <Node title="Codefresh" sub="release pipeline" show={n2} width={330} />
        <Arrow progress={a2} />
        <div
          style={{
            opacity: n3,
            transform: `scale(${0.9 + 0.1 * n3})`,
            width: 720,
            background: 'rgba(57,135,229,0.14)',
            border: `2px solid ${ACCENT}`,
            borderRadius: 18,
            padding: '24px 28px',
          }}
        >
          <div style={{fontSize: 26, color: theme.inkMuted}}>Octopus release</div>
          <div style={{fontSize: 84, fontWeight: 800, color: ACCENT, lineHeight: 1.05}}>2.5.753</div>
          <div style={{borderTop: `1px solid ${theme.border}`, marginTop: 14, paddingTop: 12}}>
            <div style={{fontSize: 24, color: theme.inkSecondary}}>Release notes</div>
            <Rise delay={170}>
              <div style={{fontSize: 28, marginTop: 6}}>• Build information: commit, branch, work item #40</div>
            </Rise>
            <Rise delay={200}>
              <div style={{fontSize: 28, marginTop: 6}}>• CI summary: codefresh/ci success</div>
            </Rise>
          </div>
        </div>
      </div>
      <Caption id="release" />
    </Scene>
  );
};

const ENVIRONMENTS = [
  {name: 'TDD', start: 40, extra: 'Smoke + acceptance tests'},
  {name: 'UAT', start: 150, extra: ''},
  {name: 'Prod', start: 250, extra: ''},
];

const EnvTile: React.FC<{name: string; start: number; extra: string}> = ({name, start, extra}) => {
  const frame = useCurrentFrame();
  const appear = useSpring(start - 30);
  const progress = interpolate(frame, [start, start + 70], [0, 1], {
    extrapolateLeft: 'clamp',
    extrapolateRight: 'clamp',
  });
  const done = progress >= 1;
  return (
    <div
      style={{
        width: 470,
        opacity: appear,
        transform: `translateY(${interpolate(appear, [0, 1], [30, 0])}px)`,
        background: done ? 'rgba(12,163,12,0.14)' : theme.surface,
        border: `2px solid ${done ? theme.good : theme.baseline}`,
        borderRadius: 20,
        padding: '26px 30px',
      }}
    >
      <div style={{display: 'flex', alignItems: 'center', justifyContent: 'space-between'}}>
        <div style={{fontSize: 64, fontWeight: 800}}>{name}</div>
        <Check show={done ? 1 : 0} size={40} />
      </div>
      <div style={{fontSize: 26, color: done ? theme.good : theme.inkMuted, marginTop: 4}}>
        {done ? 'Deployed · 2.5.753' : progress > 0 ? 'Deploying…' : 'Waiting'}
      </div>
      <div style={{height: 10, background: theme.gridline, borderRadius: 5, marginTop: 16, overflow: 'hidden'}}>
        <div style={{width: `${progress * 100}%`, height: '100%', background: done ? theme.good : ACCENT}} />
      </div>
      <div style={{fontSize: 24, color: theme.inkSecondary, marginTop: 16, lineHeight: 1.5}}>
        <div>{progress > 0.3 ? '✓' : '·'} Argo CD image tag → Git</div>
        <div>{progress > 0.8 ? '✓' : '·'} Argo CD sync: Healthy</div>
        {extra ? <div>{done ? '✓' : '·'} {extra}</div> : <div>&nbsp;</div>}
      </div>
    </div>
  );
};

/** 6. Progressive delivery (52-66 s) */
const DeliveryScene: React.FC = () => {
  const a1 = useSpring(120);
  const a2 = useSpring(225);
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>Progressive delivery · lifecycle platform-continuous</Eyebrow>
      </Rise>
      <Rise delay={4}>
        <Headline size={64}>TDD → UAT → Prod, automatically</Headline>
      </Rise>
      <div style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: 44}}>
        <EnvTile {...ENVIRONMENTS[0]} />
        <Arrow progress={a1} width={80} />
        <EnvTile {...ENVIRONMENTS[1]} />
        <Arrow progress={a2} width={80} />
        <EnvTile {...ENVIRONMENTS[2]} />
      </div>
      <Caption id="delivery" />
    </Scene>
  );
};

/** 7. Evidence and close (66-78 s) */
const EvidenceScene: React.FC = () => {
  const pos = useBoardPosition([
    [0, 2],
    [150, 3],
    [180, 4],
    [210, 5],
    [240, 6],
  ]);
  const quote = useSpring(250);
  return (
    <Scene>
      <Rise>
        <Eyebrow color={ACCENT}>Evidence and close</Eyebrow>
      </Rise>
      <div style={{display: 'flex', gap: 60, marginTop: 10}}>
        <div style={{flex: 1}}>
          <CheckRow label="Octopus API: TDD, UAT, Prod deployed" at={30} size={32} />
          <CheckRow label="Production screenshot captured" at={70} size={32} />
          <CheckRow label="Evidence comment posted on #40" at={110} size={32} />
          <CheckRow label="Issue #40 closed" at={245} size={32} />
        </div>
        <div
          style={{
            flex: 1,
            opacity: quote,
            transform: `translateY(${interpolate(quote, [0, 1], [24, 0])}px)`,
            alignSelf: 'center',
            borderLeft: `6px solid ${ACCENT}`,
            paddingLeft: 30,
          }}
        >
          <Headline size={56}>
            Evidence comes from the APIs, <span style={{color: ACCENT}}>not from the agent’s word.</span>
          </Headline>
        </div>
      </div>
      <Rise delay={10} style={{marginTop: 40}}>
        <Board position={pos} label="Instructions character counter" compact highlightFrom={0} />
      </Rise>
      <Caption id="evidence" />
    </Scene>
  );
};

/** 8. Scale and call to action (78-86 s) */
const ScaleScene: React.FC = () => {
  const frame = useCurrentFrame();
  const releases = [
    {v: '2.5.753', item: '#40 Instructions character counter'},
    {v: '2.5.754', item: '#41 Singular “work order” header'},
    {v: '2.5.755', item: '#42 Assigned date column'},
  ];
  const endCard = interpolate(frame, [165, 190], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
  return (
    <Scene>
      <div style={{opacity: 1 - endCard}}>
        <Rise>
          <Eyebrow color={ACCENT}>Scale</Eyebrow>
        </Rise>
        <Rise delay={4}>
          <Headline size={64}>Three work items, in parallel, the same afternoon</Headline>
        </Rise>
        <div style={{marginTop: 36}}>
          {releases.map((r, i) => (
            <Rise key={r.v} delay={30 + i * 18}>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 30,
                  background: theme.surface,
                  border: `1px solid ${theme.border}`,
                  borderRadius: 14,
                  padding: '16px 26px',
                  marginTop: 14,
                }}
              >
                <div style={{fontSize: 52, fontWeight: 800, color: ACCENT, width: 230}}>{r.v}</div>
                <div style={{fontSize: 30, color: theme.inkSecondary, flex: 1}}>{r.item}</div>
                <div style={{fontSize: 30, color: theme.good, fontWeight: 700}}>✓ Prod</div>
              </div>
            </Rise>
          ))}
        </div>
      </div>
      <AbsoluteFill
        style={{
          opacity: endCard,
          justifyContent: 'center',
          alignItems: 'center',
          flexDirection: 'column',
          fontFamily: theme.font,
          background: theme.plane,
        }}
      >
        <div style={{fontSize: 30, letterSpacing: 4, textTransform: 'uppercase', color: ACCENT, fontWeight: 600}}>
          AI Software Factory
        </div>
        <div style={{fontSize: 104, fontWeight: 800, marginTop: 12}}>Clear Measure</div>
        <div style={{fontSize: 40, color: theme.inkSecondary, marginTop: 36}}>
          Octopus Deploy · Codefresh · Argo CD · GitHub
        </div>
      </AbsoluteFill>
      <Caption id="scale" />
    </Scene>
  );
};

const COMPONENTS: Record<string, React.FC> = {
  hook: HookScene,
  'work-item': WorkItemScene,
  build: BuildScene,
  'ci-gate': CiGateScene,
  release: ReleaseScene,
  delivery: DeliveryScene,
  evidence: EvidenceScene,
  scale: ScaleScene,
};

export const PITCH_SCENES = SEGMENTS.map((s) => ({
  id: s.id,
  component: COMPONENTS[s.id],
  durationInFrames: s.seconds * FPS,
  audio: `audio/pitch-${s.id}.mp3`,
}));

export const PITCH_TOTAL_FRAMES = PITCH_SCENES.reduce((sum, s) => sum + s.durationInFrames, 0);

export const AiDevopsOctopusPitchVideo: React.FC = () => (
  <AbsoluteFill style={{backgroundColor: theme.plane}}>
    <Series>
      {PITCH_SCENES.map(({id, component: Component, durationInFrames, audio}) => (
        <Series.Sequence key={id} durationInFrames={durationInFrames}>
          <Fade durationInFrames={durationInFrames}>
            <Component />
          </Fade>
          <Sequence from={AUDIO_LEAD}>
            <Audio src={staticFile(audio)} />
          </Sequence>
        </Series.Sequence>
      ))}
    </Series>
  </AbsoluteFill>
);
