#!/usr/bin/env python3
"""Extract and classify every runnable test symbol in pinned upstream Expr."""

from __future__ import annotations

import json
import re
from collections import defaultdict
from pathlib import Path
from typing import Any

from common import DEFAULT_CORPUS, DEFAULT_UPSTREAM, REVISION, ROOT, read_cases

TRACEABILITY_SCHEMA = "expr.conformance.upstream-test-traceability/v1"
DEFAULT_TRACEABILITY = ROOT / "conformance" / "inventory" / "upstream-tests.json"
SYMBOL_PATTERN = re.compile(
    r"^func\s+((?:Test|Benchmark|Fuzz|Example)[A-Za-z0-9_]*)\s*\(",
    re.MULTILINE,
)
SUPPORT_PREFIXES = (
    "internal/difflib/",
    "internal/ring/",
    "internal/spew/",
    "internal/testify/",
)
POINTER_PLATFORM_SYMBOLS = {
    ("expr_test.go", "TestIssue154"),
    ("expr_test.go", "TestIssue_embedded_pointer_struct"),
    ("test/issues/836/issue_test.go", "TestIssue836"),
    ("test/issues/840/issue_test.go", "TestEnvFieldMethods"),
    ("test/issues/951/issue_test.go", "TestFieldAccessThroughEmbeddedInterface"),
    ("test/issues/951/issue_test.go", "TestFieldAccessEmbeddedInterfaceNil"),
}
DYNAMIC_METHOD_PLATFORM_SYMBOLS = {
    ("test/issues/688/issue_test.go", "TestNoInterfaceMethodWithNil"),
}
DOCGEN_PLATFORM_SYMBOLS = {
    ("docgen/docgen_test.go", "TestCreateDoc"),
    ("docgen/docgen_test.go", "TestCreateDoc_Ambiguous"),
    ("docgen/docgen_test.go", "TestCreateDoc_FromMap"),
    ("docgen/docgen_test.go", "TestContext_Markdown"),
}


def dotnet_test(path: str, symbol: str | None = None) -> dict[str, str]:
    value = {"type": "dotnet_test", "path": path}
    if symbol is not None:
        value["symbol"] = symbol
    return value


def dotnet_benchmark(path: str, symbol: str) -> dict[str, str]:
    return {"type": "dotnet_benchmark", "path": path, "symbol": symbol}


CORE_TEST_FAMILIES: dict[str, tuple[dict[str, str], ...]] = {
    "ast/find_test.go": (
        dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Parses_predicates_pointers_and_named_pointers"),
    ),
    "ast/print_test.go": (dotnet_test("tests/Expr.Tests/Syntax/SyntaxPrinterTests.cs"),),
    "ast/visitor_test.go": (dotnet_test("tests/Expr.Tests/Syntax/SyntaxWalkerTests.cs"),),
    "builtin/builtin_test.go": (
        dotnet_test("tests/Expr.Tests/Builtins/BuiltinRegistryTests.cs"),
        dotnet_test("tests/Expr.Tests/Builtins/BuiltinValueTests.cs"),
        dotnet_test("tests/Expr.Tests/Builtins/BuiltinCollectionTests.cs"),
        dotnet_test("tests/Expr.Tests/Builtins/BuiltinPredicateTests.cs"),
        dotnet_test("tests/Expr.Tests/Builtins/BuiltinSerializationAndTimeTests.cs"),
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Disabled_and_host_overridden_builtins_are_honored_on_preparsed_trees"),
        dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs"),
    ),
    "checker/checker_test.go": (
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs"),
        dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs"),
    ),
    "checker/info_test.go": (dotnet_test("tests/Expr.Tests/Compilation/CompilerTests.cs"),),
    "compiler/compiler_test.go": (
        dotnet_test("tests/Expr.Tests/Compilation/CompilerTests.cs"),
        dotnet_test("tests/Expr.Tests/Compilation/PredicateCompilerTests.cs"),
    ),
    "file/source_test.go": (
        dotnet_test("tests/Expr.Tests/Syntax/LexerTests.cs", "Locations_are_unicode_scalar_offsets"),
        dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Try_parse_returns_structured_diagnostic"),
    ),
    "optimizer/count_any_test.go": (dotnet_test("tests/Expr.Tests/Optimization/AggregateTests.cs"),),
    "optimizer/count_threshold_test.go": (dotnet_test("tests/Expr.Tests/Optimization/AggregateTests.cs"),),
    "optimizer/filter_map_test.go": (dotnet_test("tests/Expr.Tests/Optimization/MembershipAndFilterTests.cs"),),
    "optimizer/fold_test.go": (dotnet_test("tests/Expr.Tests/Optimization/ConstantFoldTests.cs"),),
    "optimizer/optimizer_test.go": (
        dotnet_test("tests/Expr.Tests/Optimization/ConstantFoldTests.cs"),
        dotnet_test("tests/Expr.Tests/Optimization/MembershipAndFilterTests.cs"),
        dotnet_test("tests/Expr.Tests/Optimization/PredicateCombinationTests.cs"),
    ),
    "optimizer/sum_array_test.go": (dotnet_test("tests/Expr.Tests/Optimization/AggregateTests.cs"),),
    "optimizer/sum_map_test.go": (dotnet_test("tests/Expr.Tests/Optimization/AggregateTests.cs"),),
    "optimizer/sum_range_test.go": (dotnet_test("tests/Expr.Tests/Optimization/AggregateTests.cs"),),
    "parser/lexer/lexer_test.go": (dotnet_test("tests/Expr.Tests/Syntax/LexerTests.cs"),),
    "parser/parser_test.go": (dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs"),),
    "patcher/value/value_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Typed_value_provider_converts_semantics_without_mutating_the_ast"),
    ),
    "patcher/with_context_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Context_patcher_prepends_cancellation_token_once"),
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Context_patcher_supports_environment_instance_methods"),
    ),
    "patcher/with_timezone_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Time_zone_patcher_adds_constant_to_date_and_now"),
    ),
    "test/interface/interface_method_test.go": (
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Environment_instance_methods_are_available_as_top_level_functions"),
    ),
    "test/interface/interface_test.go": (
        dotnet_test("tests/Expr.Security.Tests/EnvironmentSecurityTests.cs", "Reflected_schema_excludes_nonpublic_static_indexer_and_ignored_members"),
    ),
    "test/operator/issues584/issues584_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Operator_override_replaces_an_otherwise_invalid_operation"),
    ),
    "test/operator/operator_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Operator_override_replaces_an_otherwise_invalid_operation"),
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Function_overloads_choose_most_specific_signature_and_validate_variadics"),
    ),
    "test/patch/change_ident_test.go": (
        dotnet_test("tests/Expr.Tests/Syntax/SyntaxWalkerTests.cs", "Rewriter_is_non_mutating_and_patch_preserves_location"),
    ),
    "test/patch/patch_count_test.go": (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs"),
    ),
    "test/patch/patch_test.go": (
        dotnet_test("tests/Expr.Tests/Syntax/SyntaxWalkerTests.cs", "Tree_patcher_replaces_shared_target_and_minimally_copies_ancestors"),
    ),
    "test/patch/set_type/set_type_test.go": (
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Semantic_annotations_use_node_identity_without_mutating_equal_records"),
    ),
    "test/pipes/pipes_test.go": (
        dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Parses_pipe_as_first_builtin_argument"),
        dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Evaluator_ports_core_vm_operations"),
    ),
    "types/types_test.go": (dotnet_test("tests/Expr.Tests/Runtime/TypeDescriptorTests.cs"),),
    "vm/debug_test.go": (
        dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Profiling_is_per_invocation_and_does_not_mutate_the_program"),
    ),
    "vm/program_test.go": (
        dotnet_test("tests/Expr.Tests/Compilation/OpcodeAndProgramTests.cs", "Disassembler_recognizes_every_opcode"),
    ),
    "vm/runtime/helpers_test.go": (dotnet_test("tests/Expr.Tests/Runtime/ValueTests.cs"),),
    "vm/runtime/runtime_test.go": (
        dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs", "Reflected_schema_is_cached_and_honors_member_attributes"),
    ),
    "vm/vm_test.go": (
        dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs"),
        dotnet_test("tests/Expr.Tests/Execution/OpcodeExecutionTests.cs"),
        dotnet_test("tests/Expr.Tests/Execution/PredicateExecutionTests.cs"),
        dotnet_test("tests/Expr.Tests/Execution/ExecutionSecurityTests.cs"),
    ),
}

EXPR_TEST_EVIDENCE: dict[str, tuple[dict[str, str], ...]] = {}


def add_expr_evidence(symbols: tuple[str, ...], *evidence: dict[str, str]) -> None:
    for symbol in symbols:
        EXPR_TEST_EVIDENCE[symbol] = evidence


add_expr_evidence(
    (
        "TestExpr_optional_chaining_property",
        "TestExpr_optional_chaining_array",
    ),
    dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Parses_members_methods_slices_and_optional_chains"),
    dotnet_test("tests/Expr.Tests/Compilation/PredicateCompilerTests.cs", "Optional_chain_and_coalescing_share_the_chain_exit"),
)
add_expr_evidence(
    ("TestExpr_eval_with_env", "TestExpr_fetch_from_func", "TestExpr_fetch_field_from_string"),
    dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Static_environment_members_methods_and_value_providers_execute"),
)
add_expr_evidence(
    ("TestExpr_calls_with_nil", "TestExpr_call_float_arg_func_with_int", "TestFunction"),
    dotnet_test("tests/Expr.Tests/Runtime/FunctionTests.cs"),
    dotnet_test("tests/Expr.Tests/Execution/OpcodeExecutionTests.cs", "Direct_fast_and_dynamic_call_opcodes_execute_delegates"),
)
add_expr_evidence(
    ("TestPatch",),
    dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Consumer_patched_tree_can_be_compiled_through_the_facade"),
)
add_expr_evidence(
    ("TestCompile_exposed_error", "TestEval_exposed_error", "TestCompile_exposed_error_with_multiline_script"),
    dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Try_apis_return_structured_diagnostics"),
    dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Try_parse_returns_structured_diagnostic"),
)
add_expr_evidence(
    ("TestFastCall", "TestFastCall_OpCallFastErr"),
    dotnet_test("tests/Expr.Tests/Compilation/CompilerTests.cs", "Known_functions_use_specialized_calls_and_immutable_debug_tables"),
    dotnet_test("tests/Expr.Tests/Execution/OpcodeExecutionTests.cs", "Specialized_known_function_call_opcodes_execute_in_argument_order"),
)
add_expr_evidence(
    ("TestRun_custom_func_returns_an_error_as_second_arg",),
    dotnet_test("tests/Expr.Tests/Runtime/FunctionTests.cs"),
)
add_expr_evidence(
    ("TestRun_NilCoalescingOperator",),
    dotnet_test("tests/Expr.Tests/Compilation/PredicateCompilerTests.cs", "Optional_chain_and_coalescing_share_the_chain_exit"),
)
add_expr_evidence(
    ("TestEval_nil_in_maps", "TestExpr_env_types_map", "TestExpr_env_types_map_error"),
    dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Dynamic_dictionary_environment_and_host_delegate_execute"),
    dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Strict_maps_validate_known_fields_and_index_types"),
)
add_expr_evidence(
    ("TestEnv_keyword", "TestEnv_keyword_with_custom_functions"),
    dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Environment_and_env_keyword_resolve_strict_schema_members"),
)
add_expr_evidence(
    ("TestExpr_timeout",),
    dotnet_test("tests/Expr.Tests/Execution/ExecutionSecurityTests.cs", "Dynamic_regex_uses_nonbacktracking_engine_and_explicit_length_limit"),
)
add_expr_evidence(
    ("TestRaceCondition_variables",),
    dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Compiled_expression_can_run_concurrently"),
)
add_expr_evidence(
    ("TestPredicateCombination",),
    dotnet_test("tests/Expr.Tests/Optimization/PredicateCombinationTests.cs"),
)
add_expr_evidence(
    ("TestArrayComparison",),
    dotnet_test("tests/Expr.Tests/Runtime/ValueTests.cs", "Equal_matches_expr_cross_host_numeric_and_deep_collection_semantics"),
)
add_expr_evidence(
    ("TestIssue_integer_truncated_by_compiler",),
    dotnet_test("tests/Expr.Tests/Compilation/CompilerTests.cs", "Compiler_deduplicates_scalar_constants_and_resolves_all_jumps"),
)
add_expr_evidence(
    ("TestExpr_crash", "TestExpr_crash_with_zero", "TestExpr_wierd_cases"),
    dotnet_test("tests/Expr.Tests/Properties/ParserPropertyTests.cs", "Mutated_upstream_seeds_always_terminate_inside_parser_budgets"),
)
add_expr_evidence(
    ("TestIssue758_filter_map_index",),
    dotnet_test("tests/Expr.Tests/Optimization/MembershipAndFilterTests.cs", "Filter_map_fuses_projection_but_index_projection_does_not"),
)
add_expr_evidence(
    ("TestExpr_nil_op_str",),
    dotnet_test("tests/Expr.Tests/Builtins/BuiltinValueTests.cs", "String_uses_go_style_structural_collection_formatting"),
)
add_expr_evidence(
    ("TestMaxNodes", "TestMaxNodesDisabled"),
    dotnet_test("tests/Expr.Tests/Syntax/ParserTests.cs", "Enforces_node_limit"),
    dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Configuration_node_limit_is_applied_during_parsing"),
)
add_expr_evidence(
    ("TestMemoryBudget",),
    dotnet_test("tests/Expr.Tests/Execution/ExecutionSecurityTests.cs", "Allocation_charges_enforce_the_upstream_memory_budget_boundary"),
)
add_expr_evidence(
    ("TestBytesLiteral", "TestBytesLiteral_type", "TestBytesLiteral_errors"),
    dotnet_test("tests/Expr.Tests/Syntax/LexerTests.cs", "Decodes_strings_raw_strings_and_bytes"),
    dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Byte_strings_support_fetch_slice_equality_membership_and_vm_length"),
)

EXAMPLE_EVIDENCE: dict[str, tuple[dict[str, str], ...]] = {
    "ExampleEval_bytes_literal": (
        dotnet_test("tests/Expr.Tests/Execution/EvaluatorTests.cs", "Byte_strings_support_fetch_slice_equality_membership_and_vm_length"),
    ),
    "ExampleEnv": (dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Environment_function_member_overrides_builtin_syntax"),),
    "ExampleEnv_tagged_field_names": (dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs", "Reflected_schema_is_cached_and_honors_member_attributes"),),
    "ExampleEnv_hidden_tagged_field_names": (dotnet_test("tests/Expr.Security.Tests/EnvironmentSecurityTests.cs", "Reflected_schema_excludes_nonpublic_static_indexer_and_ignored_members"),),
    "ExampleWarnOnAny": (dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Expected_result_contract_can_accept_or_reject_any"),),
    "ExampleOperator": (dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Operator_override_replaces_an_otherwise_invalid_operation"),),
    "ExampleConstExpr": (dotnet_test("tests/Expr.Tests/Optimization/ConstantFoldTests.cs", "Constant_functions_fold_after_their_arguments_and_preserve_location"),),
    "ExampleAllowUndefinedVariables": (dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Undefined_variables_are_any_when_strict_checking_is_disabled"),),
    "ExampleAllowUndefinedVariables_zero_value": (dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Undefined_variables_are_any_when_strict_checking_is_disabled"),),
    "ExampleAllowUndefinedVariables_zero_value_functions": (dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Undefined_variables_are_any_when_strict_checking_is_disabled"),),
    "ExamplePatch": (dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs", "Consumer_patched_tree_can_be_compiled_through_the_facade"),),
    "ExampleWithContext": (dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Context_patcher_prepends_cancellation_token_once"),),
    "ExampleTimezone": (dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Time_zone_patcher_adds_constant_to_date_and_now"),),
}

BENCHMARK_EVIDENCE: dict[str, dict[str, str]] = {
    "Benchmark_expr": dotnet_benchmark("benchmarks/Expr.Benchmarks/CompilationBenchmarks.cs", "ColdCompilePolicy"),
    "Benchmark_expr_eval": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "Policy"),
    "Benchmark_len": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "FilterLengthOptimized"),
    "Benchmark_filter": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "Filter"),
    "Benchmark_filterLen": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "FilterLengthOptimized"),
    "Benchmark_filterMap": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "FilteredMap"),
    "Benchmark_envStruct": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "Policy"),
    "Benchmark_envMap": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "MapAccess"),
    "Benchmark_callField": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "MemberAccess"),
    "Benchmark_largeNestedStructAccess": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "MemberAccess"),
    "BenchmarkParser": dotnet_benchmark("benchmarks/Expr.Benchmarks/SyntaxBenchmarks.cs", "ParseUpstreamWorkload"),
    "BenchmarkVM": dotnet_benchmark("benchmarks/Expr.Benchmarks/EvaluationBenchmarks.cs", "Policy"),
    "BenchmarkEqual": dotnet_benchmark("benchmarks/Expr.Benchmarks/RuntimeBenchmarks.cs", "NestedEquality"),
}

PARITY_CLOSURE_EVIDENCE: dict[tuple[str, str], tuple[dict[str, str], ...]] = {
    ("expr_test.go", "ExampleAsKind"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Expected_result_contracts_cover_typed_result_examples"),
    ),
    ("expr_test.go", "ExampleOperator_with_decimal"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Operator_overrides_compose_custom_decimal_values"),
    ),
    ("expr_test.go", "TestExpr_readme_example"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Readme_function_environment_example_executes"),
    ),
    ("expr_test.go", "TestIssue_nested_closures"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
    ),
    ("expr_test.go", "TestIssue138"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Compile_time_integer_modulo_by_zero_is_rejected"),
    ),
    ("expr_test.go", "TestIssue270"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Clr_numeric_widths_and_derived_collections_match_host_regressions"),
    ),
    ("expr_test.go", "TestIssue271"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Clr_numeric_widths_and_derived_collections_match_host_regressions"),
    ),
    ("expr_test.go", "TestIssue346"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Clr_numeric_widths_and_derived_collections_match_host_regressions"),
    ),
    ("expr_test.go", "TestIssue432"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Clr_numeric_widths_and_derived_collections_match_host_regressions"),
    ),
    ("expr_test.go", "TestCompile_allow_to_use_interface_to_get_an_element_from_map"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("expr_test.go", "TestIssue401"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("expr_test.go", "TestIssue462"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Invalid_dynamic_or_strict_member_regressions_fail_deterministically"),
    ),
    ("expr_test.go", "TestIssue474"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Function_overload_numeric_rules_and_nil_arguments_match_regressions"),
    ),
    ("expr_test.go", "TestOperatorDependsOnEnv"): (
        dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Operator_override_replaces_an_otherwise_invalid_operation"),
    ),
    ("expr_test.go", "TestIssue624"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
    ),
    ("expr_test.go", "TestIssue_570"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Nullable_members_optional_chains_and_public_members_are_safe"),
    ),
    ("expr_test.go", "TestIssue785_get_nil"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
    ),
    ("expr_test.go", "TestIssue802"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("expr_test.go", "TestIssue807"): (
        dotnet_test("tests/Expr.Security.Tests/EnvironmentSecurityTests.cs", "Reflected_schema_excludes_nonpublic_static_indexer_and_ignored_members"),
    ),
    ("test/coredns/coredns_test.go", "TestCoreDNS"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "CoreDns_compile_corpus_accepts_registered_host_functions"),
    ),
    ("test/issues/567/issue_test.go", "TestIssue567"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Disassembly_retains_builtin_identity"),
    ),
    ("test/issues/688/issue_test.go", "TestNoInterfaceMethodWithNil_with_any"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Function_overload_numeric_rules_and_nil_arguments_match_regressions"),
    ),
    ("test/issues/688/issue_test.go", "TestNoInterfaceMethodWithNil_with_env"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Typed_interface_methods_accept_nil_arguments"),
    ),
    ("test/issues/723/issue_test.go", "TestIssue723"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("test/issues/730/issue_test.go", "TestIssue730"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Nullable_enum_conversion_and_dynamic_comparison_match_host_regressions"),
    ),
    ("test/issues/730/issue_test.go", "TestIssue730_eval"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Nullable_enum_conversion_and_dynamic_comparison_match_host_regressions"),
    ),
    ("test/issues/730/issue_test.go", "TestIssue730_warn_about_different_types"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Nullable_enum_conversion_and_dynamic_comparison_match_host_regressions"),
    ),
    ("test/issues/739/issue_test.go", "TestIssue739"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("test/issues/756/issue_test.go", "TestIssue756"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Context_is_injected_into_environment_and_nested_object_methods"),
    ),
    ("test/issues/785/issue_test.go", "TestIssue785"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
    ),
    ("test/issues/817/issue_test.go", "TestIssue817_2"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Function_overload_numeric_rules_and_nil_arguments_match_regressions"),
    ),
    ("test/issues/817/issue_test.go", "TestIssue817_1"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Variadic_format_function_receives_nil_without_coercion"),
    ),
    ("test/issues/819/issue_test.go", "TestIssue819"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Upstream_pure_regression_expressions_evaluate"),
    ),
    ("test/issues/823/issue_test.go", "TestIssue823"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Context_is_injected_into_nested_registered_functions"),
    ),
    ("test/issues/823/issue_test.go", "TestIssue823_EnvMethods"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Context_is_injected_into_environment_and_nested_object_methods"),
    ),
    ("test/issues/830/issue_test.go", "TestIssue830"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Undefined_boolean_result_uses_expected_type_default"),
    ),
    ("test/issues/844/issue_test.go", "TestIssue844"): (
        dotnet_test("tests/Expr.Security.Tests/EnvironmentSecurityTests.cs", "Reflected_schema_excludes_nonpublic_static_indexer_and_ignored_members"),
    ),
    ("test/issues/854/issue_test.go", "TestIssue854"): (
        dotnet_test("tests/Expr.Tests/Checking/CheckerTests.cs", "Strict_maps_validate_known_fields_and_index_types"),
    ),
    ("test/issues/857/issue_test.go", "TestIssue857"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs", "Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values"),
    ),
    ("test/issues/888/issue_test.go", "TestIssue888"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Variadic_host_method_accepts_multiple_arguments"),
    ),
    ("test/issues/924/issue_test.go", "TestIssue924_allow_disabling_builtins_and_providing_fn_at_runtime"): (
        dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Dynamic_runtime_function_can_replace_disabled_builtin"),
    ),
    ("expr_test.go", "TestExpr_map_default_values"): (
        dotnet_test("tests/Expr.Tests/Parity/MapDefaultValueParityTests.cs", "TestExpr_map_default_values"),
    ),
    ("expr_test.go", "TestExpr_map_default_values_compile_check"): (
        dotnet_test("tests/Expr.Tests/Parity/MapDefaultValueParityTests.cs", "TestExpr_map_default_values_compile_check"),
    ),
    ("expr_test.go", "TestIssue105"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamHostTypeParityTests.cs", "Issue105_explicit_schema_promotes_unambiguous_nested_members"),
    ),
    ("test/crowdsec/crowdsec_test.go", "TestCrowdsec"): (
        dotnet_test("tests/Expr.Tests/Parity/CrowdsecCorpusTests.cs", "TestCrowdsec"),
    ),
    ("test/gen/gen_test.go", "TestGenerated"): (
        dotnet_test("tests/Expr.Tests/Parity/GeneratedCorpusTests.cs", "TestGenerated"),
    ),
    ("test/issues/461/issue_test.go", "TestIssue461"): (
        dotnet_test("tests/Expr.Tests/Parity/UpstreamHostTypeParityTests.cs", "Issue461_nominal_string_wrapper_is_not_interchangeable_with_string"),
    ),
}

for constant_error_symbol in (
    "TestConstExpr_error_panic",
    "TestConstExpr_error_as_error",
    "TestConstExpr_error_wrong_type",
    "TestConstExpr_error_no_env",
):
    PARITY_CLOSURE_EVIDENCE[("expr_test.go", constant_error_symbol)] = (
        dotnet_test(
            "tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs",
            "Constant_function_failures_and_registration_errors_are_structured",
        ),
    )

for typed_result_symbol in (
    "ExampleAsBool",
    "ExampleAsBool_error",
    "ExampleAsInt",
    "ExampleAsInt64",
    "ExampleAsFloat64",
    "ExampleAsFloat64_error",
    "TestAsBool_exposed_error",
):
    PARITY_CLOSURE_EVIDENCE[("expr_test.go", typed_result_symbol)] = (
        dotnet_test(
            "tests/Expr.Tests/Parity/UpstreamRegressionParityTests.cs",
            "Expected_result_contracts_cover_typed_result_examples",
        ),
    )

PARITY_BENCHMARK_FAMILIES: dict[str, dict[str, str]] = {
    "bench_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunUpstreamVariant",
    ),
    "checker/checker_bench_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "CheckUpstreamVariant",
    ),
    "optimizer/count_any_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunOptimizerVariant",
    ),
    "optimizer/count_threshold_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunOptimizerVariant",
    ),
    "optimizer/sum_array_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunOptimizerVariant",
    ),
    "optimizer/sum_range_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunOptimizerVariant",
    ),
    "patcher/value/bench_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "RunValueProviderVariant",
    ),
    "test/bench/bench_call_test.go": dotnet_benchmark(
        "benchmarks/Expr.Benchmarks/UpstreamParityBenchmarks.cs",
        "CompileUpstreamVariant",
    ),
}


def symbol_kind(symbol: str) -> str:
    if symbol.startswith("Test"):
        return "test"
    if symbol.startswith("Benchmark"):
        return "benchmark"
    if symbol.startswith("Fuzz"):
        return "fuzz"
    return "example"


def extract_symbols(upstream: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in sorted(upstream.rglob("*_test.go")):
        relative = path.relative_to(upstream).as_posix()
        source = path.read_text(encoding="utf-8")
        for match in SYMBOL_PATTERN.finditer(source):
            symbol = match.group(1)
            rows.append(
                {
                    "path": relative,
                    "symbol": symbol,
                    "kind": symbol_kind(symbol),
                    "line": source.count("\n", 0, match.start()) + 1,
                }
            )
    return rows


def corpus_evidence(corpus: Path) -> dict[tuple[str, str], list[str]]:
    evidence: dict[tuple[str, str], list[str]] = defaultdict(list)
    for case in read_cases(corpus):
        provenance = case["provenance"]
        evidence[(provenance["path"], provenance["test"])].append(case["id"])
    return evidence


def classify(row: dict[str, Any], corpus: dict[tuple[str, str], list[str]]) -> dict[str, Any]:
    path = row["path"]
    symbol = row["symbol"]
    result = dict(row)
    ids = corpus.get((path, symbol))
    if ids:
        result.update(
            disposition="differential_corpus",
            granularity="symbol",
            evidence=[{"type": "corpus", "ids": ids}],
            note="Pinned Go oracle outcomes are executed by the .NET differential suite.",
        )
        return result

    if path.startswith(SUPPORT_PREFIXES):
        result.update(
            disposition="excluded_support",
            granularity="symbol",
            evidence=[],
            note="Tests an embedded Go support package (testify, spew, difflib, or ring), not Expr language behavior.",
        )
        return result

    if path in {"internal/deref/deref_test.go", "test/deref/deref_test.go"}:
        result.update(
            disposition="platform_mapping",
            granularity="file_family",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Go pointer dereference suites",
                },
                dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs"),
            ],
            note="Go pointer-chain dereferencing maps to CLR reference/nullability and explicit host adapters.",
        )
        return result

    if (path, symbol) == ("builtin/builtin_test.go", "TestBuiltin_with_deref"):
        result.update(
            disposition="platform_mapping",
            granularity="symbol",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Go pointer dereference suites",
                },
                dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs"),
                dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Typed_value_provider_converts_semantics_without_mutating_the_ast"),
            ],
            note="Go pointer dereferencing maps to CLR host adapters and value providers before built-in invocation.",
        )
        return result

    if (path, symbol) in POINTER_PLATFORM_SYMBOLS:
        result.update(
            disposition="platform_mapping",
            granularity="symbol",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Go pointer dereference suites",
                },
                dotnet_test("tests/Expr.Tests/Runtime/EnvironmentSchemaTests.cs"),
                dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Nullable_members_optional_chains_and_public_members_are_safe"),
            ],
            note="Go pointer, embedded-pointer, and embedded-interface promotion maps to CLR references, nullable descriptors, and explicit host schemas.",
        )
        return result

    if (path, symbol) in DYNAMIC_METHOD_PLATFORM_SYMBOLS:
        result.update(
            disposition="platform_mapping",
            granularity="symbol",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Dynamic host method access",
                },
                dotnet_test("tests/Expr.Tests/Parity/HostEnvironmentParityTests.cs", "Typed_interface_methods_accept_nil_arguments"),
            ],
            note="Unschematized runtime method discovery is intentionally disabled; the typed schema path preserves the nil-argument behavior.",
        )
        return result

    if (path, symbol) == ("test/issues/934/issue_test.go", "TestIssue934"):
        result.update(
            disposition="platform_mapping",
            granularity="symbol",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Dynamic host member access",
                },
                dotnet_test(
                    "tests/Expr.Security.Tests/UpstreamSecurityMappingTests.cs",
                    "Issue934_any_typed_values_cannot_discover_public_or_nonpublic_members",
                ),
            ],
            note="Unschematized runtime member discovery is intentionally disabled; typed schemas preserve explicit public-member access.",
        )
        return result

    if (path, symbol) in DOCGEN_PLATFORM_SYMBOLS:
        result.update(
            disposition="platform_mapping",
            granularity="symbol",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/upstream-test-traceability.md",
                    "anchor": "Go documentation generator",
                },
                dotnet_test("tests/Expr.Tests/Parity/DocumentationPlatformParityTests.cs"),
            ],
            note="Go Markdown doc generation maps to .NET schema metadata, explicit ambiguity handling, and compiler-generated XML documentation.",
        )
        return result

    if path == "test/time/time_test.go":
        result.update(
            disposition="platform_mapping",
            granularity="file_family",
            evidence=[
                {
                    "type": "documentation",
                    "path": "docs/compatibility.md",
                    "anchor": "`time.Duration` values use `TimeSpan`",
                },
                dotnet_test("tests/Expr.Tests/Builtins/BuiltinSerializationAndTimeTests.cs"),
                dotnet_test("tests/Expr.Tests/Execution/OpcodeExecutionTests.cs", "Time_and_duration_arithmetic_matches_upstream_runtime_helpers"),
            ],
            note="Expression behavior is tested through the documented TimeSpan, DateTimeOffset, and TimeZoneInfo mappings.",
        )
        return result

    parity_evidence = PARITY_CLOSURE_EVIDENCE.get((path, symbol))
    if parity_evidence is not None:
        result.update(
            disposition="dotnet_test",
            granularity="symbol",
            evidence=list(parity_evidence),
            note="The linked focused .NET regression executes this upstream symbol's observable contract.",
        )
        return result

    if row["kind"] == "test" and path in CORE_TEST_FAMILIES:
        result.update(
            disposition="dotnet_test",
            granularity="file_family",
            evidence=list(CORE_TEST_FAMILIES[path]),
            note="The upstream file's test family maps to the linked focused .NET suite.",
        )
        return result

    if path == "expr_test.go" and symbol in EXPR_TEST_EVIDENCE:
        result.update(
            disposition="dotnet_test",
            granularity="symbol",
            evidence=list(EXPR_TEST_EVIDENCE[symbol]),
            note="The linked .NET regression covers this named upstream behavior.",
        )
        return result

    if path == "expr_test.go" and symbol in EXAMPLE_EVIDENCE:
        result.update(
            disposition="dotnet_test",
            granularity="symbol",
            evidence=list(EXAMPLE_EVIDENCE[symbol]),
            note="The example's observable contract is covered by the linked .NET test.",
        )
        return result

    if path == "patcher/value/value_example_test.go" and symbol == "ExampleAnyValuer":
        result.update(
            disposition="dotnet_test",
            granularity="symbol",
            evidence=[dotnet_test("tests/Expr.Tests/Patching/SemanticPatcherTests.cs", "Typed_value_provider_converts_semantics_without_mutating_the_ast")],
            note="The .NET value-provider contract replaces Go's AnyValuer example.",
        )
        return result

    if path == "test/examples/examples_test.go" and symbol == "TestExamples":
        result.update(
            disposition="dotnet_test",
            granularity="file_family",
            evidence=[dotnet_test("tests/Expr.Tests/Facade/ExprEngineTests.cs")],
            note="The public API examples are compiled and executed by the facade suite; Go-specific examples remain individually inventoried.",
        )
        return result

    if symbol == "FuzzExpr":
        result.update(
            disposition="dotnet_test",
            granularity="symbol",
            evidence=[
                dotnet_test("tests/Expr.Tests/Properties/ParserPropertyTests.cs", "Mutated_upstream_seeds_always_terminate_inside_parser_budgets"),
                {"type": "fuzz_harness", "path": "tools/Expr.Fuzz/Program.cs"},
            ],
            note="Deterministic CI mutations and the standalone long-running harness replace Go fuzz execution.",
        )
        return result

    if row["kind"] == "benchmark" and symbol in BENCHMARK_EVIDENCE:
        result.update(
            disposition="dotnet_benchmark",
            granularity="symbol",
            evidence=[BENCHMARK_EVIDENCE[symbol]],
            note="A corresponding BenchmarkDotNet workload exists; results are platform-specific rather than cross-runtime comparable.",
        )
        return result

    if row["kind"] == "benchmark" and path in PARITY_BENCHMARK_FAMILIES:
        result.update(
            disposition="dotnet_benchmark",
            granularity="symbol",
            evidence=[PARITY_BENCHMARK_FAMILIES[path]],
            note="The linked parameterized BenchmarkDotNet family includes the equivalent workload variant; results remain platform-specific.",
        )
        return result

    reason = {
        "benchmark": "No equivalent BenchmarkDotNet workload is linked yet.",
        "example": "No exact executable .NET example or focused test is linked yet.",
        "fuzz": "No equivalent .NET fuzz/property harness is linked yet.",
        "test": "No exact .NET regression, differential case, or reviewed platform mapping is linked yet.",
    }[row["kind"]]
    result.update(
        disposition="gap",
        granularity="symbol",
        evidence=[],
        note=reason,
    )
    return result


def build_inventory(upstream: Path = DEFAULT_UPSTREAM, corpus: Path = DEFAULT_CORPUS) -> dict[str, Any]:
    rows = [classify(row, corpus_evidence(corpus)) for row in extract_symbols(upstream)]
    source_files = sorted(path.relative_to(upstream).as_posix() for path in upstream.rglob("*_test.go"))
    symbol_files = {row["path"] for row in rows}
    counts: dict[str, int] = defaultdict(int)
    for row in rows:
        counts[row["disposition"]] += 1
    return {
        "schema": TRACEABILITY_SCHEMA,
        "revision": REVISION,
        "sourceGlob": "**/*_test.go",
        "sourceFileCount": len(source_files),
        "filesWithoutSymbols": [path for path in source_files if path not in symbol_files],
        "symbolCount": len(rows),
        "dispositionCounts": dict(sorted(counts.items())),
        "symbols": rows,
    }


def encode_inventory(inventory: dict[str, Any]) -> str:
    return json.dumps(inventory, ensure_ascii=False, indent=2) + "\n"
