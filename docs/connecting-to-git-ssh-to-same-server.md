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
