type Binding = { fetch(request: Request): Promise<Response> };

export default {
  fetch(request: Request, env: { ACCOUNT_API: Binding; ASSETS: Binding }) {
    const binding = new URL(request.url).pathname.startsWith('/api/')
      ? env.ACCOUNT_API
      : env.ASSETS;
    return binding.fetch(request);
  }
};
