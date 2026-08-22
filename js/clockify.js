/**
 * @typedef {Object} ProjectRef
 * @property {string} id - Project ID
 * @property {string} name - Project name
 */

/**
 * @typedef {Object} TaskRef
 * @property {string} id - Task ID
 * @property {string} name - Task name
 */

/**
 * @typedef {Object} CacheEntry
 * @property {ProjectRef} project - Project details
 * @property {TaskRef} task - Task details
 */

const clockify = (function () {
    const baseUrl = "https://app.clockify.me/api";
    /**
     * @typedef {Object} User
     * @property {string} id - The unique Clockify user ID
     * @property {string} activeWorkspace - The ID of the user's currently active workspace,
     *   used to construct API endpoint paths (e.g. `/workspaces/{activeWorkspace}/...`)
     */

    /** @type {User} */
    const user = JSON.parse(localStorage.getItem("user"));
    const cachedProjects = {};
    const dateFormatter = new Intl.DateTimeFormat("en-GB", {
        hour: "numeric",
        minute: "numeric",
        hour12: false,
        weekday: "short",
        day: "numeric",
        month: "short",
        timeZone: "Asia/Jakarta",
    });

    /**
     * Try to get task metadata within the project
     * 
     * @param {string} projectName
     * @param {string} taskName
     * @returns {Promise<CacheEntry>}
     */
    async function tryGetFromCache(projectName, taskName) {
        if (!Object.hasOwn(cachedProjects, projectName)) {
            const temp = await getProjectByName(projectName);
            if (temp !== null) {
                const foundProject = {
                    name: temp.name,
                    id: temp.id,
                    tasks: [],
                };
                const tasks = await getProjectTasks(foundProject.id);
                tasks.forEach((t) =>
                    foundProject.tasks.push({ name: t.name, id: t.id }),
                );
                cachedProjects[projectName] = foundProject;
            } else {
                throw `Project ${projectName} is not found`;
            }
        }

        const cachedProject = cachedProjects[projectName];
        const matchingTasks = cachedProject.tasks.filter(
            (t) => t.name === taskName,
        );
        if (matchingTasks.length === 0) {
            throw `Task ${taskName} does not exist within ${projectName}`;
        }

        return {
            project: {
                id: cachedProject.id,
                name: cachedProject.name,
            },
            task: matchingTasks[0],
        };
    }

    async function parseReplacement(replacements) {
        const result = {};
        for (const map of replacements) {
            const from = await tryGetFromCache(map.from.project, map.from.task);
            result[`${from.project.id}|${from.task.id}`] = await tryGetFromCache(map.to.project, map.to.task);
        }

        return result;
    }

    const createDefaultHeaders = function () {
        return {
            Accept: "application/json",
            "App-Name": "WEB",
            "App-Version": "1.3653.0",
            "Content-Type": "application/json",
            "X-Auth-Token": localStorage.getItem("token"),
            "X-Auth-Checksum": localStorage.getItem("checksum"),
            "User-Agent":
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36 Edg/145.0.0.0",
        };
    };

    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

    /**
     * @typedef {Object} DateRangeFilter
     * @property {string} from - Date string parseable by Date constructor
     * @property {string} to - Date string parseable by Date constructor
     */

    /**
     * @typedef {Object} ReplacementMapping
     * @property {{ project: string, task: string }} from - Source project/task names
     * @property {{ project: string, task: string }} to - Target project/task names
     */

    /**
     * Bulk updates task entries within a date range by replacing their project and task
     * according to the provided replacement mappings.
     *
     * @param {DateRangeFilter} filter - Date range to filter entries
     * @param {ReplacementMapping[]} replacements - List of project/task replacements
     * @returns {Promise<void>}
     */
    async function bulkUpdate(filter, replacements) {
        filter.from = new Date(filter.from);
        filter.to = new Date(filter.to);
        const replacementSettings = await parseReplacement(replacements);
        let page = 0;
        while (page <= 10) {
            const entries = await getEntries(page);
            let foundMatchingTimeRange = false;
            for (const entry of entries.timeEntriesList) {
                const range = entry.timeInterval;
                const startTime = new Date(range.start),
                    endTime = new Date(range.end);
                if (!(filter.from <= startTime && startTime <= filter.to))
                    continue;

                foundMatchingTimeRange = true;
                const taskKey = `${entry.task.projectId}|${entry.task.id}`;
                if (!Object.hasOwn(replacementSettings, taskKey)) continue;

                const replacement = replacementSettings[taskKey];
                console.log(
                    `Changing ${entry.description} at ${dateFormatter.formatRange(startTime, endTime)}
                    from ${entry.project.name} | ${entry.task.name}
                    to ${replacement.project.name} | ${replacement.task.name}`,
                );
                await changeEntryProjectAndTask(
                    entry.id,
                    replacement.project.id,
                    replacement.task.id,
                );
                await sleep(100);
            }

            if (!foundMatchingTimeRange) {
                console.log(
                    `Found no matching entry from time range, exiting...`,
                );
                break;
            }

            page++;
            await sleep(1000);
        }
    }

    /**
     * @typedef {Object} AddBulkEntry
     * @property {string} description - Task description (e.g. "Technical Implementation")
     * @property {string} start - Time string (e.g. "08:00")
     * @property {string} end - Time string (e.g. "11:30")
     * @property {string} project - Exact project name in Clockify
     * @property {string} task - Exact task name within the project
     */
    
    /**
     * Bulk adds task entries for a given date.
     *
     * @param {AddBulkEntry[]} entries - List of entries to add
     * @param {string} [date] - Date string in "YYYY-M-D" format. Defaults to today if omitted.
     * @returns {Promise<void>}
     */
    async function bulkAdd(entries, date) {
        if (date === undefined || date === null) {
            const now = new Date();
            date = `${now.getFullYear()}-${now.getMonth() + 1}-${now.getDate()}`;
        }

        for (const entry of entries) {
            const meta = await tryGetFromCache(entry.project, entry.task);
            const start = new Date(`${date} ${entry.start}`);
            const end = new Date(`${date} ${entry.end}`);
            await addEntry({
                billable: false,
                description: entry.description,
                projectId: meta.project.id,
                taskId: meta.task.id,
                tagIds: null,
                customFields: [],
                start: start.toISOString(),
                end: end.toISOString(),
                type: "REGULAR",
            });
            await sleep(500);
        }
    }

    /**
     * @typedef {Object} AddSingleEntry
     * @property {boolean} billable
     * @property {string} description - Task description
     * @property {string} projectId - Project ID
     * @property {string} taskId - Task ID
     * @property {string} start - Start time in ISO format
     * @property {string} end - End time in ISO format
     */

    /**
     * Creates a new task entry in Clockify.
     *
     * @param {AddSingleEntry} entry
     * @returns {Promise<Object>} The created time entry response from the API
     */
    async function addEntry(entry) {
        const response = await fetch(
            `${baseUrl}/workspaces/${user.activeWorkspace}/timeEntries/full`,
            {
                method: "POST",
                headers: createDefaultHeaders(),
                body: JSON.stringify(entry),
            },
        );
        return await response.json();
    }

    /**
     * @typedef {Object} TimeInterval
     * @property {string} start - ISO 8601 date string
     * @property {string} end - ISO 8601 date string
     */

    /**
     * @typedef {Object} TimeEntry
     * @property {string} id
     * @property {string} description
     * @property {ProjectRef} project
     * @property {TaskRef & { projectId: string }} task
     * @property {TimeInterval} timeInterval
     */

    /**
     * Retrieves a paginated list of task entries for the current user.
     *
     * @param {number} [page=1] - Page number (0-indexed in practice)
     * @param {number} [limit=50] - Number of entries per page
     * @returns {Promise<{ timeEntriesList: TimeEntry[] }>}
     */
    async function getEntries(page = 1, limit = 50) {
        const response = await fetch(
            `${baseUrl}/workspaces/${user.activeWorkspace}/timeEntries/user/${user.id}/full?page=${page}&limit=${limit}`,
            {
                method: "GET",
                headers: createDefaultHeaders(),
            },
        );
        return await response.json();
    }

    /**
     * Searches for a project by its exact name.
     *
     * @param {string} name - Exact project name to search for
     * @returns {Promise<ProjectRef | null>} The matching project, or null if not found
     */
    async function getProjectByName(name) {
        const response = await fetch(
            `${baseUrl}/workspaces/${user.activeWorkspace}/project-picker/clients?page=1&excludedProjects=&excludedTasks=&search=${encodeURI(name)}&userId=&archived=false`,
            {
                method: "GET",
                headers: createDefaultHeaders(),
            },
        );
        const result = await response.json();
        for (const record of result) {
            for (const project of record.projects) {
                if (name === project.name) {
                    return project;
                }
            }
        }

        return null;
    }

    /**
     * Retrieves all tasks for a given project.
     *
     * @param {string} projectId - The project's Clockify ID
     * @returns {Promise<Array<TaskRef>>} List of tasks
     */
    async function getProjectTasks(projectId) {
        const response = await fetch(
            `${baseUrl}/workspaces/${user.activeWorkspace}/project-picker/projects/${projectId}/tasks?excludedTasks=&page=1&search=&userId=&taskFilterEnabled=`,
            {
                method: "GET",
                headers: createDefaultHeaders(),
            },
        );
        return await response.json();
    }

    /**
     * Changes the project and task of an existing task entry.
     *
     * @param {string} entryId - The task entry's Clockify ID
     * @param {string} targetProjectId - The target project's Clockify ID
     * @param {string} targetTaskId - The target task's Clockify ID
     * @returns {Promise<Object>} The updated time entry response from the API
     */
    async function changeEntryProjectAndTask(
        entryId,
        targetProjectId,
        targetTaskId,
    ) {
        const response = await fetch(
            `${baseUrl}/workspaces/${user.activeWorkspace}/timeEntries/${entryId}/projectAndTask`,
            {
                method: "PUT",
                headers: createDefaultHeaders(),
                body: JSON.stringify({
                    projectId: targetProjectId,
                    taskId: targetTaskId,
                }),
            },
        );
        return await response.json();
    }

    return { bulkUpdate, bulkAdd, addEntry, getEntries, getProjectByName, getProjectTasks, changeEntryProjectAndTask };
})();

/* Sample usage */
/*
await clockify.bulkAdd([
    {
        description: "User feature",
        start: "08:00",
        end: "11:30",
        project: "CRM App",
        task: "Product Development",
    },
    {
        description: "Prayer + break",
        start: "11:30",
        end: "13:00",
        project: "General",
        task: "Lunch/Break",
    },
    {
        description: "Technical Implementation",
        start: "13:00",
        end: "14:30",
        project: "CRM App",
        task: "Product Development",
    },
    {
        description: "Daily meeting",
        start: "14:30",
        end: "15:00",
        project: "CRM App",
        task: "Product Development",
    },
    {
        description: "Identity management",
        start: "15:00",
        end: "16:00",
        project: "CRM App",
        task: "Product Development",
    },
]);

await clockify.bulkUpdate({ from: "2026-01-01", to: "2026-03-31" }, [
    {
        from: {
            project: "CRM App",
            task: "Product Development",
        },
        to: {
            project: "CRM vNext",
            task: "Legacy Maintenance",
        },
    },
    {
        from: {
            project: "CRM App",
            task: "Project Meeting/Administration",
        },
        to: {
            project: "CRM vNext",
            task: "Legacy Discussion",
        },
    },
    {
        from: {
            project: "General",
            task: "Lunch/Break"
        },
        to: {
            project: "General Engineering",
            task: "Lunch/Break"
        }
    }
]);
*/