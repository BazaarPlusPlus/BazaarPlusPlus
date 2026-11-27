import { decodeRunPayload } from '../../bazaarplusplus-site/src/features/runs/run-payload.ts';
import { buildDecisionPath } from '../../packages/runs/message-stream/decision-path';
import { FIXTURE_RUN_ID } from '../../bazaarplusplus-site/tools/run-fixture/build.ts';
import { readFileSync, writeFileSync } from 'node:fs';

const source = 'bazaarplusplus-site/test/fixtures/run-stream.payload.b64';
const detail = await decodeRunPayload(
  Buffer.from(readFileSync('../' + source, 'utf8'), 'base64'),
  FIXTURE_RUN_ID
);
const path = await buildDecisionPath(detail);
const visit = path?.visits.find(
  (v) =>
    v.day === 1 &&
    v.hour === 1 &&
    v.actions.filter((a) => a.verb === 'bought').length >= 2
);
if (!visit || !path)
  throw new Error('Expected two purchases in the first shop');
const moments = visit.actions
  .filter((a) => a.verb === 'bought')
  .slice(0, 2)
  .map((action) => {
    const seq = action.order;
    const moment = path.moments.get(seq);
    if (!moment?.snapshot) throw new Error('Missing snapshot at ' + seq);
    return {
      seq,
      action: action.verb,
      card: action.card?.templateId,
      snapshot: moment.snapshot,
      frame: path.stream.readableFrame(path.stream.frame(seq)!)
    };
  });
writeFileSync(
  'src/demo-data/run.json',
  JSON.stringify(
    { source, day: visit.day, hour: visit.hour, kind: visit.kind, moments },
    null,
    2
  ) + '\n'
);
