# GitHub Issues

Plans, feature requests, and bugs for every project live as issues in the one monorepo repository. Run `gh` inside the clone so it infers the repository.

- Pass multi-line bodies through a heredoc.
- Listing for triage or bulk reading needs the fields projected explicitly, since `gh` omits comments and flattens labels by default:

  ```bash
  gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'
  ```

- Triage runs over issues only; external pull requests are not a request surface.
- "Publish to the issue tracker" means `gh issue create`; "fetch the relevant ticket" means `gh issue view <number> --comments`.
