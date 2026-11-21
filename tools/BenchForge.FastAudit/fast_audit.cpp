// BenchForge.FastAudit — zero-dependency C++20 native helper for high-frequency
// repeated repository audits (source-neutrality scan + requirement path checks).
// Build: g++ -O3 -std=c++20 -pthread tools/BenchForge.FastAudit/fast_audit.cpp -o .tools/fast-audit/benchforge-fast-audit

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace fs = std::filesystem;

namespace {

struct Finding {
    std::string path;
    int line;
    std::string kind;
};

static std::string to_lower_ascii(std::string_view input) {
    std::string out;
    out.resize(input.size());
    for (size_t i = 0; i < input.size(); ++i) {
        unsigned char c = static_cast<unsigned char>(input[i]);
        out[i] = static_cast<char>((c >= 'A' && c <= 'Z') ? (c - 'A' + 'a') : c);
    }
    return out;
}

static std::string json_escape(std::string_view input) {
    std::string out;
    out.reserve(input.size() + 8);
    for (unsigned char c : input) {
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b"; break;
            case '\f': out += "\\f"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            default:
                if (c < 32 || c > 126) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", static_cast<unsigned>(c));
                    out += buf;
                } else {
                    out.push_back(static_cast<char>(c));
                }
                break;
        }
    }
    return out;
}

static std::vector<std::string> git_tracked_files(const fs::path& root) {
    std::vector<std::string> files;
#ifdef _WIN32
    std::string cmd = "git -C \"" + root.string() + "\" ls-files -z";
    FILE* pipe = _popen(cmd.c_str(), "rb");
#else
    std::string cmd = "git -C \"" + root.string() + "\" ls-files -z";
    FILE* pipe = popen(cmd.c_str(), "r");
#endif
    if (!pipe) {
        throw std::runtime_error("Failed to invoke git ls-files");
    }
    std::string buffer;
    char chunk[16384];
    while (size_t n = std::fread(chunk, 1, sizeof(chunk), pipe)) {
        buffer.append(chunk, n);
    }
#ifdef _WIN32
    int status = _pclose(pipe);
#else
    int status = pclose(pipe);
#endif
    if (status != 0) {
        throw std::runtime_error("git ls-files returned non-zero status");
    }
    size_t start = 0;
    while (start < buffer.size()) {
        size_t end = buffer.find('\0', start);
        if (end == std::string::npos) end = buffer.size();
        if (end > start) {
            files.emplace_back(buffer.data() + start, end - start);
        }
        start = end + 1;
    }
    return files;
}

static int run_neutrality(const fs::path& root, bool strict, const std::string& output_path, unsigned jobs) {
    // Construct needle cleanly to keep source-neutrality audit self-clean.
    const std::string needle = std::string("py") + "measure";
    const std::string legal_register = "THIRD_PARTY_REVIEW.md";

    auto tracked = git_tracked_files(root);
    std::vector<std::vector<Finding>> per_file(tracked.size());
    std::atomic<size_t> next_index{0};

    unsigned workers = std::max(1u, std::min<unsigned>(jobs, static_cast<unsigned>(std::max<size_t>(1, tracked.size()))));
    std::vector<std::thread> threads;
    threads.reserve(workers);

    for (unsigned t = 0; t < workers; ++t) {
        threads.emplace_back([&]() {
            while (true) {
                size_t idx = next_index.fetch_add(1, std::memory_order_relaxed);
                if (idx >= tracked.size()) break;
                const std::string& rel = tracked[idx];
                if (rel == legal_register) continue;

                auto& out = per_file[idx];
                if (to_lower_ascii(rel).find(needle) != std::string::npos) {
                    out.push_back(Finding{rel, 0, "filename"});
                }
                fs::path full = root / fs::path(rel);
                std::error_code ec;
                if (!fs::is_regular_file(full, ec) || ec) continue;

                std::ifstream in(full, std::ios::binary);
                if (!in) continue;
                std::string raw((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
                if (raw.find('\0') != std::string::npos) continue;

                // Fast case-insensitive pre-check before splitting lines.
                std::string lowered = to_lower_ascii(raw);
                if (lowered.find(needle) == std::string::npos) continue;

                int line_no = 1;
                size_t pos = 0;
                while (pos < lowered.size()) {
                    size_t nl = lowered.find('\n', pos);
                    if (nl == std::string::npos) nl = lowered.size();
                    std::string_view line_view(lowered.data() + pos, nl - pos);
                    if (line_view.find(needle) != std::string_view::npos) {
                        out.push_back(Finding{rel, line_no, "content"});
                    }
                    ++line_no;
                    pos = (nl < lowered.size()) ? nl + 1 : lowered.size();
                }
            }
        });
    }
    for (auto& th : threads) th.join();

    std::vector<Finding> findings;
    for (auto& bucket : per_file) {
        for (auto& f : bucket) findings.push_back(std::move(f));
    }

    if (!output_path.empty()) {
        fs::path out_file(output_path);
        if (out_file.has_parent_path()) {
            fs::create_directories(out_file.parent_path());
        }
        std::ofstream os(out_file, std::ios::binary | std::ios::trunc);
        os << "{\n";
        os << "  \"allowedRegister\": \"" << legal_register << "\",\n";
        os << "  \"violations\": " << findings.size() << ",\n";
        os << "  \"findings\": [";
        if (!findings.empty()) {
            os << "\n";
            for (size_t i = 0; i < findings.size(); ++i) {
                os << "    {\"path\": \"" << json_escape(findings[i].path)
                   << "\", \"line\": " << findings[i].line
                   << ", \"kind\": \"" << findings[i].kind << "\"}";
                if (i + 1 < findings.size()) os << ",";
                os << "\n";
            }
            os << "  ";
        }
        os << "]\n}\n";
    }

    std::cout << "Source-neutrality audit: " << findings.size() << " violation(s)\n";
    for (size_t i = 0; i < std::min<size_t>(40, findings.size()); ++i) {
        std::cout << "  " << findings[i].path << ":" << findings[i].line
                  << " (" << findings[i].kind << ")\n";
    }
    return (strict && !findings.empty()) ? 1 : 0;
}

static std::vector<std::vector<std::string>> parse_csv(const fs::path& file) {
    std::ifstream in(file, std::ios::binary);
    if (!in) throw std::runtime_error("Cannot open CSV: " + file.string());
    std::string text((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    std::vector<std::vector<std::string>> rows;
    std::vector<std::string> row;
    std::string field;
    bool in_quotes = false;
    for (size_t i = 0; i < text.size(); ++i) {
        char c = text[i];
        if (in_quotes) {
            if (c == '"' && i + 1 < text.size() && text[i + 1] == '"') {
                field.push_back('"');
                ++i;
            } else if (c == '"') {
                in_quotes = false;
            } else {
                field.push_back(c);
            }
        } else {
            if (c == '"') {
                in_quotes = true;
            } else if (c == ',') {
                row.push_back(std::move(field));
                field.clear();
            } else if (c == '\r') {
                continue;
            } else if (c == '\n') {
                row.push_back(std::move(field));
                field.clear();
                if (!(row.size() == 1 && row[0].empty())) rows.push_back(std::move(row));
                row.clear();
            } else {
                field.push_back(c);
            }
        }
    }
    if (!field.empty() || !row.empty()) {
        row.push_back(std::move(field));
        rows.push_back(std::move(row));
    }
    return rows;
}

static int run_paths(const fs::path& root) {
    struct CheckTarget {
        std::string csv_rel;
        std::vector<std::string> columns;
    };
    const std::vector<CheckTarget> targets = {
        {"requirements/traceability.csv", {"implementation_file", "verification_file"}},
        {"requirements/legacy-runner-audit.csv", {"source_evidence", "test_evidence"}}
    };

    std::vector<std::string> errors;
    size_t checked = 0;
    for (const auto& target : targets) {
        auto rows = parse_csv(root / target.csv_rel);
        if (rows.empty()) {
            errors.push_back("Empty CSV: " + target.csv_rel);
            continue;
        }
        const auto& header = rows[0];
        std::vector<std::pair<std::string, size_t>> col_indices;
        size_t id_col = 0;
        for (size_t c = 0; c < header.size(); ++c) {
            if (header[c] == "requirement_id") id_col = c;
            for (const auto& wanted : target.columns) {
                if (header[c] == wanted) col_indices.emplace_back(wanted, c);
            }
        }
        for (size_t r = 1; r < rows.size(); ++r) {
            const auto& row = rows[r];
            std::string uid = (id_col < row.size()) ? row[id_col] : ("row-" + std::to_string(r));
            for (const auto& [col_name, idx] : col_indices) {
                if (idx >= row.size()) continue;
                const std::string& rel = row[idx];
                if (rel.empty()) continue;
                ++checked;
                if (rel.find('\\') != std::string::npos) {
                    errors.push_back(uid + ": " + col_name + " must use forward slashes: " + rel);
                } else {
                    std::error_code ec;
                    if (!fs::is_regular_file(root / fs::path(rel), ec) || ec) {
                        errors.push_back(uid + ": missing " + col_name + ": " + rel);
                    }
                }
            }
        }
    }

    if (!errors.empty()) {
        for (const auto& err : errors) std::cerr << "ERROR: " << err << "\n";
        return 1;
    }
    std::cout << "Requirement paths OK: " << checked << " path(s) verified with POSIX separators.\n";
    return 0;
}

}  // namespace

int main(int argc, char** argv) {
    try {
        fs::path root = fs::current_path();
        std::string command = "all";
        std::string output_path;
        bool strict = false;
        unsigned jobs = std::max(1u, std::min(8u, std::thread::hardware_concurrency()));

        std::vector<std::string> positional;
        for (int i = 1; i < argc; ++i) {
            std::string arg = argv[i];
            if (arg == "--strict") {
                strict = true;
            } else if (arg == "--root" && i + 1 < argc) {
                root = fs::absolute(argv[++i]);
            } else if (arg == "--output" && i + 1 < argc) {
                output_path = argv[++i];
            } else if (arg == "--jobs" && i + 1 < argc) {
                jobs = static_cast<unsigned>(std::clamp(std::stoi(argv[++i]), 1, 256));
            } else if (arg == "--help" || arg == "-h") {
                std::cout << "Usage: benchforge-fast-audit [neutrality|paths|all] [--root PATH] [--strict] [--output PATH] [--jobs N]\n";
                return 0;
            } else {
                positional.push_back(arg);
            }
        }
        if (!positional.empty()) command = positional[0];

        if (command == "neutrality") {
            return run_neutrality(root, strict, output_path, jobs);
        }
        if (command == "paths") {
            return run_paths(root);
        }
        if (command == "all") {
            int r1 = run_neutrality(root, strict, output_path, jobs);
            int r2 = run_paths(root);
            return (r1 != 0 || r2 != 0) ? 1 : 0;
        }
        std::cerr << "Unknown command: " << command << "\n";
        return 2;
    } catch (const std::exception& ex) {
        std::cerr << "ERROR: " << ex.what() << "\n";
        return 2;
    }
}
