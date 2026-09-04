package com.nalo.medquery.domain.entity;

import com.nalo.medquery.domain.model.AddressData;

import jakarta.persistence.Embeddable;
import jakarta.validation.Valid;
import lombok.AllArgsConstructor;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Embeddable
@Getter
@NoArgsConstructor
@AllArgsConstructor
public class AddressEntity {

    private String logradouro;
    private String bairro;
    private String cep;
    private String numero;
    private String complemento;
    private String cidade;
    private String uf;


    public AddressEntity(AddressData address){
        this.logradouro = address.logradouro();
        this.bairro = address.bairro();
        this.cep = address.cep();
        this.numero = address.numero();
        this.complemento = address.complemento();
        this.cidade = address.cidade();
        this.uf = address.uf();
    }


    public void updateAddressInformation(AddressData endereco) {
        if (endereco.logradouro() != null) {
            this.logradouro = endereco.logradouro();
        }
        if (endereco.bairro() != null) {
            this.bairro = endereco.bairro();
        }
        if (endereco.cep() != null) {
            this.cep = endereco.cep();
        }
        if (endereco.numero() != null) {
            this.numero = endereco.numero();
        }
        if (endereco.complemento() != null) {
            this.complemento = endereco.complemento();
        }
        if (endereco.cidade() != null) {
            this.cidade = endereco.cidade();
        }
        if (endereco.uf() != null) {
            this.uf = endereco.uf();
        }
    }
}
