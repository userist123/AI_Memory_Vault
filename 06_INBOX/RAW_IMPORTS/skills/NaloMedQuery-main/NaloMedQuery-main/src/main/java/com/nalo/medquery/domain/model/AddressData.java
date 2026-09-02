package com.nalo.medquery.domain.model;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;

public record AddressData(
    @NotBlank
    String logradouro,
    @NotBlank
    String bairro,
    @NotBlank
    @Pattern(regexp = "\\d{8}")
    String cep,
    @NotBlank
    String cidade,
    @NotBlank
    String uf,
    @NotBlank
    String numero,
    String complemento
) {}
