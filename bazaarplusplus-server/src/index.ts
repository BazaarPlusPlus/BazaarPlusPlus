import { WorkerEntrypoint } from 'cloudflare:workers';
import type { Env } from './env';
import { fetch } from './http/route-shell';
import { handleWebApiRequest } from './modules/web-account-api';
import { scheduled } from './scheduled';

export class WebApi extends WorkerEntrypoint<Env> {
  override fetch(request: Request): Promise<Response> {
    return handleWebApiRequest(request, this.env);
  }
}

export default { fetch, scheduled } satisfies ExportedHandler<Env>;
