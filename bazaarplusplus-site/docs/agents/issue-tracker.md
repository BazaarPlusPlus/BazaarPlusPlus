# GitHub Issues

Run `gh` inside the clone so it infers the repository.

- Pass multi-line bodies through a heredoc.
- Listing for triage or bulk reading needs the fields projected explicitly, since `gh` omits comments and flattens labels by default:

  ```bash
  gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'
  ```
- External PRs are not a request surface here — triage runs over issues only.
- “Publish to the issue tracker” means create an issue; “fetch the relevant ticket” means `gh issue view <number> --comments`.
