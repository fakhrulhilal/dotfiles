# Connecting to git repository targeting same server through SSH

This guide assumes we have generated all required ssh keys. In this sample, we generate 2 keys for personal and office 
matter (f.e. `ssh-keyget -t rsa -b 4096 -f ~/.ssh/id_personal`).

## Using global ssh config

Suppose we have 2 repositories, one repository targeting Azure DevOps using personal account, and 
the other one targeting Azure DevOps but using office account, then we can use global ssh config as 
follows
<details>
    <summary>Sample of <code>~/.ssh/config</code></summary>

```
Host personal
    Hostname ssh.dev.azure.com
    HostkeyAlgorithms +ssh-rsa
    KexAlgorithms +diffie-hellman-group-exchange-sha256
    # Azure DevOps currently only supports RSA algorithm
    IdentityFile "~/.ssh/id_personal"

Host office
    Hostname ssh.dev.azure.com
    HostkeyAlgorithms +ssh-rsa
    KexAlgorithms +diffie-hellman-group-exchange-sha256
    # Azure DevOps currently only supports RSA algorithm
    IdentityFile "~/.ssh/id_office"
```
</details>

Then we have to adjust the remote url, instead of `git@ssh.dev.azure.com:v3/MyOrg/MyProject/MyRepo`, but use 
`git@personal:v3/MyOrg/MyProject/Myrepo`. Do the same thing with the other repository as well.

## Using global git config

Suppose we have all git repositories for work projects located under `~/Project/Jobs`, and we want to use personal 
account for the rest, then we can configure config specific only for that folder, keeping the rest as default.

<details>
    <summary>Sample configurations</summary>

`~/.gitconfig`
```
[IncludeIf "gitdir:~/Projects/Jobs/"]
    path = ~/Project/Jobs/git.txt
```

`~/Projects/Jobs/git.txt`
```
[core]
    sshCommand = ssh -i ~/.ssh/id_office -o IdentitiesOnly=yes

[user]
    name = Last, First
    email = MyUser@my-office.com
```

</details>

> NOTE: It's good to have global ssh config for Azure DevOps, as it only supports RSA algorithm only at the moment.

## Using global ssh config

This approach is similar to global git config, except, we rely on ssh config rather than git config. The 
problem with git config, it appends ssh command globally, and doesn't respect more closer config, there is 
no overriding rule in git config. That's why we use this approach. Taking example of previous scenario, we 
can use this config

<details>
    <summary>Sample configurations</summary>

`~/.ssh/config`
```
Match host ssh.dev.azure.com exec "pwd | grep -qi '/projects/jobs/'"
    IdentityFile ~/.ssh/id_office
    IdentitiesOnly yes

Include "~/Projects/Jobs/ssh.txt"
```

`~/Projects/Jobs/ssh.txt`
```
# Default identity for all
IdentityFile "~/.ssh/id_ed25519"

Host ssh.dev.azure.com *.visualstudio.com
    Hostname ssh.dev.azure.com
    HostkeyAlgorithms +ssh-rsa
    #MACs +hmac-sha2-512,+hmac-sha2-256
    KexAlgorithms +diffie-hellman-group-exchange-sha256
    # Default identity for Azure DevOps for all repositories
    IdentityFile "~/.ssh/id_rsa"
    IdentitiesOnly yes
```

</details>

Technically, ssh will offer all keys in following order:
- `~/.ssh/id_office`
- `~/.ssh/id_rsa`
- `~/.ssh/id_ed25519`

So it depends on our Azure DevOps, which key we're storing in their server. If first key accepted, then it 
will be used as authentication. This means, first key pair matches win (not the opposite). The same rule 
applies when configuring ssh. We should include global config as the last over specific rule.

## Specific config per repository

Suppose a repository connect to different Azure DevOps remote, such as an office is moving to new organization due to 
acquisition. Let's assume the new organization is the default, so we will configure only the old organization only for 
each repository.

<details>
    <summary>Sample configurations</summary>

`path/to/repository/.git/config`
```
# the new org, assumed to be the default
[remote "origin"]
	url = git@ssh.dev.azure.com:v3/NewOrg/MyProject/MyRepo
	fetch = +refs/heads/*:refs/remotes/origin/*
[remote "legacy"]
	url = git@ssh.dev.azure.com:v3/OldOrg/MyProject/MyRepo
	fetch = +refs/heads/*:refs/remotes/legacy/*
	sshCommand = ssh -i ~/.ssh/id_office_old -o IdentitiesOnly=yes
[branch "master"]
	remote = origin
	merge = refs/heads/master

```

</details>

The default repository config can follow one of above step.
